// Checkers Web UI client

const PRESETS = {
  initial: "B:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12",
  spec: "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16",
  tablebase: "W:WK1,5,10:BK12,15"
};

let currentBoardState = {
  turn: "B",
  squares: {} // 1..32 -> { color: 'W'|'B', king: boolean }
};

let selectedSquare = null;
let boardFlipped = false;
let highlightedSquares = [];

// Square to (row, col) mapping:
function squareToCoords(s) {
  const idx = s - 1;
  const r = Math.floor(idx / 4);
  const c = (r % 2 === 0) ? (idx % 4) * 2 + 1 : (idx % 4) * 2;
  return { r, c };
}

function coordsToSquare(r, c) {
  if (r < 0 || r >= 8 || c < 0 || c >= 8) return 0;
  if ((r + c) % 2 === 0) return 0;
  return r * 4 + Math.floor(c / 2) + 1;
}

// Parse PDN string into currentBoardState
function parsePdn(pdn) {
  const parts = pdn.trim().split(":");
  if (parts.length < 3) return false;

  const turn = parts[0].toUpperCase().startsWith("B") ? "B" : "W";
  const newSquares = {};

  for (let i = 1; i < parts.length; i++) {
    const sec = parts[i].trim();
    if (!sec) continue;
    const colorChar = sec[0].toUpperCase();
    const color = colorChar === "W" ? "W" : (colorChar === "B" ? "B" : null);
    if (!color) continue;

    const list = sec.substring(1).split(",");
    for (const item of list) {
      const clean = item.trim();
      if (!clean) continue;
      const isKing = clean.toUpperCase().startsWith("K");
      const sqNum = parseInt(clean.replace(/[Kk]/g, ""), 10);
      if (sqNum >= 1 && sqNum <= 32) {
        newSquares[sqNum] = { color, king: isKing };
      }
    }
  }

  currentBoardState = { turn, squares: newSquares };
  return true;
}

// Generate canonical PDN from state
function stateToPdn() {
  const wPieces = [];
  const bPieces = [];

  for (let s = 1; s <= 32; s++) {
    const p = currentBoardState.squares[s];
    if (!p) continue;
    const label = p.king ? `K${s}` : `${s}`;
    if (p.color === "W") wPieces.push(label);
    else if (p.color === "B") bPieces.push(label);
  }

  return `${currentBoardState.turn}:W${wPieces.join(",")}:B${bPieces.join(",")}`;
}

// Render board DOM
function renderBoard() {
  const boardEl = document.getElementById("checkersBoard");
  boardEl.innerHTML = "";

  document.getElementById("pdnInput").value = stateToPdn();
  updateTurnDisplay();

  for (let visualRow = 0; visualRow < 8; visualRow++) {
    for (let visualCol = 0; visualCol < 8; visualCol++) {
      const r = boardFlipped ? (7 - visualRow) : visualRow;
      const c = boardFlipped ? (7 - visualCol) : visualCol;

      const sqNum = coordsToSquare(r, c);
      const isPlayableDark = sqNum > 0;

      const sqEl = document.createElement("div");
      sqEl.className = `square ${isPlayableDark ? "dark playable" : "light"}`;

      if (isPlayableDark) {
        sqEl.dataset.square = sqNum;

        // Square number badge
        const numEl = document.createElement("span");
        numEl.className = "square-number";
        numEl.textContent = sqNum;
        sqEl.appendChild(numEl);

        // Selection / highlight styles
        if (selectedSquare === sqNum) {
          sqEl.classList.add("selected");
        } else if (highlightedSquares.includes(sqNum)) {
          sqEl.classList.add("highlight");
        }

        // Piece
        const piece = currentBoardState.squares[sqNum];
        if (piece) {
          const pieceEl = document.createElement("div");
          pieceEl.className = `piece ${piece.color === "B" ? "black" : "white"} ${piece.king ? "king" : ""}`;
          sqEl.appendChild(pieceEl);
        }

        sqEl.addEventListener("click", () => handleSquareClick(sqNum));
      }

      boardEl.appendChild(sqEl);
    }
  }
}

function updateTurnDisplay() {
  const turnText = document.getElementById("currentTurnText");
  if (currentBoardState.turn === "B") {
    turnText.textContent = "Black";
    turnText.className = "badge black";
  } else {
    turnText.textContent = "White";
    turnText.className = "badge white";
  }
}

// Interactive clicking on squares
function handleSquareClick(sqNum) {
  const piece = currentBoardState.squares[sqNum];

  // If clicked own piece, select it
  if (piece && piece.color === currentBoardState.turn) {
    selectedSquare = sqNum;
    renderBoard();
    return;
  }

  // If already selected a piece and clicked destination square
  if (selectedSquare !== null) {
    const fromSq = selectedSquare;
    const toSq = sqNum;

    // Check if move is legal via API or execute
    const moveStr = `${fromSq}-${toSq}`;
    executePlayerMove(fromSq, toSq, moveStr);
    selectedSquare = null;
  }
}

async function executePlayerMove(fromSq, toSq, moveStr) {
  const movingPiece = currentBoardState.squares[fromSq];
  if (!movingPiece) return;

  // Optimistically validate via validate endpoint
  try {
    const valRes = await fetch("/v1/move/validate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ position: stateToPdn(), move: moveStr })
    });

    const valData = await valRes.json();
    if (!valData.legal) {
      showValidationResult(false, `Move ${moveStr} is illegal!`);
      renderBoard();
      return;
    }
  } catch (e) {
    console.warn("Validation error, proceeding optimistically", e);
  }

  // Apply move
  delete currentBoardState.squares[fromSq];

  // Check captured piece (if jump)
  const coordsFrom = squareToCoords(fromSq);
  const coordsTo = squareToCoords(toSq);
  if (Math.abs(coordsFrom.r - coordsTo.r) === 2) {
    const midR = (coordsFrom.r + coordsTo.r) / 2;
    const midC = (coordsFrom.c + coordsTo.c) / 2;
    const midSq = coordsToSquare(midR, midC);
    if (midSq > 0) delete currentBoardState.squares[midSq];
  }

  // Check crowning
  let isKing = movingPiece.king;
  if (!isKing) {
    if (movingPiece.color === "B" && toSq >= 29 && toSq <= 32) isKing = true;
    if (movingPiece.color === "W" && toSq >= 1 && toSq <= 4) isKing = true;
  }

  currentBoardState.squares[toSq] = { color: movingPiece.color, king: isKing };
  currentBoardState.turn = currentBoardState.turn === "B" ? "W" : "B";
  highlightedSquares = [fromSq, toSq];

  renderBoard();

  // If auto bot is active, trigger bot suggest move
  if (document.getElementById("autoBotToggle").checked) {
    setTimeout(requestEngineMove, 300);
  }
}

// Request best move from API
async function requestEngineMove() {
  const btn = document.getElementById("btnSuggest");
  btn.disabled = true;
  btn.textContent = "Analyzing...";

  const position = stateToPdn();
  const level = document.getElementById("levelSelect").value;
  const softTimeMs = parseInt(document.getElementById("softTimeInput").value, 10) || 250;
  const hardTimeMs = parseInt(document.getElementById("hardTimeInput").value, 10) || 1200;
  const maxDepth = parseInt(document.getElementById("maxDepthInput").value, 10) || 12;

  const requestBody = {
    gameId: "checkers-8x8",
    state: { notation: "PDN", position },
    level,
    limits: { maxDepth, softTimeMs, hardTimeMs }
  };

  try {
    const startTime = performance.now();
    const res = await fetch("/v1/move/suggest", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(requestBody)
    });

    const elapsed = Math.round(performance.now() - startTime);
    const data = await res.json();

    if (!res.ok) {
      alert(`API Error (${res.status}): ${data.error || "Unknown error"}`);
      return;
    }

    // Update telemetry
    document.getElementById("resBestMove").textContent = data.bestMove || "-";
    document.getElementById("resTime").textContent = `${data.info?.timeMs ?? elapsed} ms`;
    document.getElementById("resTablebaseHit").textContent = data.info?.tablebaseHit ? "TRUE 🎯" : "False";
    document.getElementById("resTablebaseHit").style.color = data.info?.tablebaseHit ? "#34d399" : "";
    document.getElementById("resDepth").textContent = data.depth ?? "-";
    document.getElementById("resNodes").textContent = (data.nodes || 0).toLocaleString();
    document.getElementById("resScore").textContent = data.scoreOrWDL ?? "-";
    document.getElementById("resPv").textContent = data.pv ? data.pv.join(" → ") : "-";
    document.getElementById("rawJsonResponse").textContent = JSON.stringify(data, null, 2);

    // Highlight best move squares
    if (data.bestMove) {
      const parts = data.bestMove.split(/[-x]/).map(s => parseInt(s, 10)).filter(s => !isNaN(s));
      highlightedSquares = parts;
      renderBoard();
    }
  } catch (err) {
    alert("Network or Server error: " + err.message);
  } finally {
    btn.disabled = false;
    btn.innerHTML = '<span class="btn-icon">⚡</span> Get Best Move (POST /v1/move/suggest)';
  }
}

// Validate move via API
async function validateMove() {
  const move = document.getElementById("validateMoveInput").value.trim();
  if (!move) return;

  const position = stateToPdn();
  try {
    const res = await fetch("/v1/move/validate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ position, move })
    });
    const data = await res.json();
    showValidationResult(data.legal, data.legal ? `Move '${move}' is LEGAL!` : (data.error || `Move '${move}' is ILLEGAL!`));
  } catch (e) {
    showValidationResult(false, "Validation request failed: " + e.message);
  }
}

function showValidationResult(isValid, message) {
  const el = document.getElementById("validationResult");
  el.className = `validation-result ${isValid ? "valid" : "invalid"}`;
  el.textContent = message;
  el.classList.remove("hidden");
}

// Health check
async function checkHealth() {
  const badge = document.getElementById("healthBadge");
  const text = document.getElementById("healthText");
  try {
    const res = await fetch("/healthz");
    const data = await res.json();
    if (data.ok) {
      badge.className = "health-badge ok";
      text.textContent = `OK (${data.workers} Chinook workers)`;
    } else {
      badge.className = "health-badge error";
      text.textContent = "Degraded";
    }
  } catch {
    badge.className = "health-badge error";
    text.textContent = "Offline";
  }
}

// Event Listeners setup
document.addEventListener("DOMContentLoaded", () => {
  parsePdn(PRESETS.spec);
  renderBoard();
  checkHealth();

  document.getElementById("btnSuggest").addEventListener("click", requestEngineMove);
  document.getElementById("btnValidate").addEventListener("click", validateMove);

  document.getElementById("btnFlip").addEventListener("click", () => {
    boardFlipped = !boardFlipped;
    renderBoard();
  });

  document.getElementById("btnReset").addEventListener("click", () => {
    parsePdn(PRESETS.initial);
    highlightedSquares = [];
    renderBoard();
  });

  document.getElementById("btnLoadPdn").addEventListener("click", () => {
    const input = document.getElementById("pdnInput").value;
    if (parsePdn(input)) {
      highlightedSquares = [];
      renderBoard();
    } else {
      alert("Invalid PDN format!");
    }
  });

  document.querySelectorAll("[data-preset]").forEach(btn => {
    btn.addEventListener("click", (e) => {
      const presetName = e.target.getAttribute("data-preset");
      if (PRESETS[presetName]) {
        parsePdn(PRESETS[presetName]);
        highlightedSquares = [];
        renderBoard();
      }
    });
  });

  document.getElementById("levelSelect").addEventListener("change", (e) => {
    const val = e.target.value;
    if (val === "weak") {
      document.getElementById("softTimeInput").value = 100;
      document.getElementById("maxDepthInput").value = 8;
    } else if (val === "medium") {
      document.getElementById("softTimeInput").value = 250;
      document.getElementById("maxDepthInput").value = 12;
    } else if (val === "strong") {
      document.getElementById("softTimeInput").value = 550;
      document.getElementById("maxDepthInput").value = 16;
    }
  });
});
