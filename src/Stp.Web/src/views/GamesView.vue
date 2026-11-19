<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import {
  Volume2,
  VolumeX,
  EyeOff,
  ArrowLeft,
  RotateCcw,
  Sparkles,
  Users,
  Play,
  BookOpen,
  X,
} from '@lucide/vue'

const route = useRoute()

// Sound FX & Renderers
let sfxModule: any = null
let caroRenderer: any = null
let ludoRenderer: any = null
let snakeRenderer: any = null
let game2048Renderer: any = null

const isMuted = ref(true)
const isBossScreen = ref(false)
const isBookOpen = ref(false)
const activeGame = computed(() => (route.params.gameSlug as string) || '')

// Canvas element ref
const gameCanvas = ref<HTMLCanvasElement | null>(null)

// Game definitions
interface GameInfo {
  slug: string
  title: string
  subtitle: string
  icon: string
  tag: string
  color: string
  description: string
  players: string
}

const games: GameInfo[] = [
  {
    slug: 'caro',
    title: 'Caro Nông Trại',
    subtitle: 'Hoa Hướng Dương & Cà Rốt',
    icon: '🌻',
    tag: 'Online + Offline',
    color: '#e67e22',
    description: 'Đấu trí 15x15 trên nền đất nông trại ấm áp. Cây lớn dần 5 nấc, thắng cuộc hoa nở tưng bừng kết hợp pháo hoa nông sản!',
    players: '1 - 2 người',
  },
  {
    slug: 'co-ca-ngua',
    title: 'Cờ Cá Ngựa Nông Trại',
    subtitle: 'Đua Đàn Gà 4 Màu',
    icon: '🎲',
    tag: '2 - 4 người / Bot',
    color: '#27ae60',
    description: 'Tung xúc xắc đưa 4 chú gà con màu vàng, đỏ, xanh lá, xanh dương về chuồng ấm. Đá đối phương văng lông vũ tơi bời!',
    players: '2 - 4 người',
  },
  {
    slug: 'dan-ga-con',
    title: 'Đàn Gà Con',
    subtitle: 'Rắn Săn Nông Sản',
    icon: '🐥',
    tag: 'Thư giãn nhanh',
    color: '#f39c12',
    description: 'Gà mẹ dẫn đầu đàn con nhấp nhô ăn 4 loại nông sản tươi ngon (ngô, cà rốt, củ cải, bí đỏ). Tùy chọn đi xuyên hàng rào!',
    players: 'Chơi đơn',
  },
  {
    slug: 'gop-nong-san',
    title: 'Gộp Nông Sản',
    subtitle: '2048 Mầm Cây Đất Cày',
    icon: '🌽',
    tag: 'Giải đố 4x4',
    color: '#8e44ad',
    description: 'Trượt ghép các hạt mầm nông nghiệp từ Cải con (4) lên đến Bí Ngô Vàng khổng lồ (2048). Có nút Hoàn tác (Undo) và Sách sưu tập!',
    players: 'Chơi đơn',
  },
]

// ==========================================
// 1. CARO STATE & LOGIC
// ==========================================
const caroMode = ref<'bot' | 'pvp'>('bot')
const caroBotDiff = ref<'easy' | 'medium' | 'hard'>('medium')
const caroTurn = ref<'X' | 'O'>('X')
const caroWinner = ref<string | null>(null)
const caroBoard = ref<Array<Array<{ symbol: 'X' | 'O' | null; turn: number }>>>(
  Array(15).fill(null).map(() => Array(15).fill(null).map(() => ({ symbol: null, turn: 0 })))
)
const caroWinLine = ref<Array<[number, number]>>([])
const caroMoveCount = ref(0)
const caroIsAiThinking = ref(false)
const caroLastMove = ref<{ r: number; c: number; player: string } | null>(null)

function initCaroGame() {
  caroBoard.value = Array(15).fill(null).map(() => Array(15).fill(null).map(() => ({ symbol: null, turn: 0 })))
  caroTurn.value = 'X'
  caroWinner.value = null
  caroWinLine.value = []
  caroMoveCount.value = 0
  caroIsAiThinking.value = false
  caroLastMove.value = null
  syncCaroRenderer()
}

function syncCaroRenderer() {
  if (!caroRenderer) return
  const boardDto = caroBoard.value.map(row =>
    row.map(cell => (cell.symbol ? cell.symbol : ' '))
  )
  caroRenderer.updateState({
    board: boardDto,
    currentTurn: caroTurn.value,
    winner: caroWinner.value,
    hasWon: caroWinner.value !== null,
    winningLine: caroWinLine.value.map(([r, c]) => ({ r, c })),
    lastMove: caroLastMove.value,
    isAiThinking: caroIsAiThinking.value,
    aiLevel: caroBotDiff.value,
  })
}

function handleCaroCellClick(row: number, col: number) {
  if (caroWinner.value || caroIsAiThinking.value) return
  if (caroBoard.value[row][col].symbol) return

  makeCaroMove(row, col, caroTurn.value)

  if (!caroWinner.value && caroMode.value === 'bot' && caroTurn.value === 'O') {
    caroIsAiThinking.value = true
    syncCaroRenderer()
    setTimeout(() => {
      aiMakeCaroMove()
      caroIsAiThinking.value = false
      syncCaroRenderer()
    }, 450)
  }
}

function makeCaroMove(r: number, c: number, sym: 'X' | 'O') {
  caroMoveCount.value++
  caroBoard.value[r][c] = { symbol: sym, turn: caroMoveCount.value }
  caroLastMove.value = { r, c, player: sym }

  // Check 5 in a row
  const win = checkCaroWin(r, c, sym)
  if (win) {
    caroWinner.value = sym
    caroWinLine.value = win
  } else if (caroMoveCount.value >= 15 * 15) {
    caroWinner.value = 'Draw'
  } else {
    caroTurn.value = caroTurn.value === 'X' ? 'O' : 'X'
  }
  syncCaroRenderer()
}

function checkCaroWin(r: number, c: number, sym: 'X' | 'O'): Array<[number, number]> | null {
  const dirs = [
    [0, 1],
    [1, 0],
    [1, 1],
    [1, -1],
  ]
  for (const [dr, dc] of dirs) {
    const line: Array<[number, number]> = [[r, c]]
    // forward
    let step = 1
    while (true) {
      const nr = r + dr * step
      const nc = c + dc * step
      if (nr < 0 || nr >= 15 || nc < 0 || nc >= 15) break
      if (caroBoard.value[nr][nc].symbol === sym) {
        line.push([nr, nc])
        step++
      } else break
    }
    // backward
    step = 1
    while (true) {
      const nr = r - dr * step
      const nc = c - dc * step
      if (nr < 0 || nr >= 15 || nc < 0 || nc >= 15) break
      if (caroBoard.value[nr][nc].symbol === sym) {
        line.push([nr, nc])
        step++
      } else break
    }
    if (line.length >= 5) return line
  }
  return null
}

function aiMakeCaroMove() {
  if (caroWinner.value) return
  // Heuristic evaluation or simple Minimax
  let bestScore = -999999
  let bestMove: [number, number] | null = null

  // Evaluate cells adjacent to existing pieces
  const candidates: Array<[number, number]> = []
  for (let r = 0; r < 15; r++) {
    for (let c = 0; c < 15; c++) {
      if (!caroBoard.value[r][c].symbol) {
        let hasNeighbor = false
        for (let dr = -2; dr <= 2; dr++) {
          for (let dc = -2; dc <= 2; dc++) {
            const nr = r + dr
            const nc = c + dc
            if (nr >= 0 && nr < 15 && nc >= 0 && nc < 15 && caroBoard.value[nr][nc].symbol) {
              hasNeighbor = true
              break
            }
          }
          if (hasNeighbor) break
        }
        if (hasNeighbor) candidates.push([r, c])
      }
    }
  }

  if (candidates.length === 0) {
    candidates.push([7, 7])
  }

  // Score each candidate
  for (const [r, c] of candidates) {
    const attack = evaluateCaroCell(r, c, 'O')
    const defense = evaluateCaroCell(r, c, 'X')
    const score = attack * 1.2 + defense
    if (score > bestScore) {
      bestScore = score
      bestMove = [r, c]
    }
  }

  if (bestMove) {
    makeCaroMove(bestMove[0], bestMove[1], 'O')
  }
}

function evaluateCaroCell(r: number, c: number, sym: 'X' | 'O'): number {
  let total = 0
  const dirs = [[0, 1], [1, 0], [1, 1], [1, -1]]
  for (const [dr, dc] of dirs) {
    let count = 0
    let openEnds = 0
    for (let s = 1; s <= 4; s++) {
      const nr = r + dr * s
      const nc = c + dc * s
      if (nr < 0 || nr >= 15 || nc < 0 || nc >= 15) break
      if (caroBoard.value[nr][nc].symbol === sym) count++
      else if (!caroBoard.value[nr][nc].symbol) { openEnds++; break }
      else break
    }
    for (let s = 1; s <= 4; s++) {
      const nr = r - dr * s
      const nc = c - dc * s
      if (nr < 0 || nr >= 15 || nc < 0 || nc >= 15) break
      if (caroBoard.value[nr][nc].symbol === sym) count++
      else if (!caroBoard.value[nr][nc].symbol) { openEnds++; break }
      else break
    }
    if (count >= 4) total += 100000
    else if (count === 3 && openEnds >= 2) total += 5000
    else if (count === 3) total += 1000
    else if (count === 2 && openEnds >= 2) total += 500
    else if (count === 1) total += 20
  }
  return total
}

// ==========================================
// 2. LUDO STATE & LOGIC
// ==========================================
const ludoPlayers = ref<any[]>([
  { color: 0, isHuman: true, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
  { color: 1, isHuman: false, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
  { color: 2, isHuman: false, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
  { color: 3, isHuman: false, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
])
const ludoCurrentTurn = ref(0)
const ludoDiceRoll = ref(0)
const ludoCanRoll = ref(true)
const ludoWinner = ref<number | null>(null)
const ludoPlayerCount = ref(4)

function updateLudoPieceCoords() {
  ludoPlayers.value.forEach(player => {
    player.pieces.forEach((piece: any) => {
      if (piece.state === 0) {
        piece.trackPos = -1
        piece.trackPosition = -1
        piece.homeStep = -1
        piece.homeTrackStep = -1
      } else if (piece.state === 1) {
        const tp = (piece.step + player.color * 13) % 52
        piece.trackPos = tp
        piece.trackPosition = tp
        piece.homeStep = -1
        piece.homeTrackStep = -1
      } else if (piece.state === 2) {
        piece.trackPos = -1
        piece.trackPosition = -1
        const hs = Math.max(0, Math.min(5, piece.step - 50))
        piece.homeStep = hs
        piece.homeTrackStep = hs
      } else if (piece.state === 3) {
        piece.trackPos = -1
        piece.trackPosition = -1
        piece.homeStep = 5
        piece.homeTrackStep = 5
      }
    })
  })
}

function initLudoGame() {
  ludoPlayers.value = [
    { color: 0, isHuman: true, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
    { color: 1, isHuman: false, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
    { color: 2, isHuman: false, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
    { color: 3, isHuman: false, pieces: [{ id: 0, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 1, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 2, state: 0, step: -1, trackPos: -1, homeStep: -1 }, { id: 3, state: 0, step: -1, trackPos: -1, homeStep: -1 }] },
  ]
  ludoCurrentTurn.value = 0
  ludoDiceRoll.value = 0
  ludoCanRoll.value = true
  ludoWinner.value = null
  updateLudoPieceCoords()
  syncLudoRenderer()
}

function syncLudoRenderer(legalMoves: any[] = []) {
  if (!ludoRenderer) return
  updateLudoPieceCoords()
  ludoRenderer.updateState({
    players: ludoPlayers.value,
    currentTurnColor: ludoCurrentTurn.value,
    currentTurn: ludoCurrentTurn.value,
    canRoll: ludoCanRoll.value,
    lastDiceRoll: ludoDiceRoll.value,
    winner: ludoWinner.value,
    isGameOver: ludoWinner.value !== null,
  }, legalMoves)
  if (typeof ludoRenderer.setLegalMoves === 'function') {
    ludoRenderer.setLegalMoves(legalMoves)
  }
}

function handleLudoRollDice() {
  if (!ludoCanRoll.value || ludoWinner.value !== null) return
  ludoCanRoll.value = false

  const roll = Math.floor(Math.random() * 6) + 1
  ludoDiceRoll.value = roll

  if (ludoRenderer) {
    ludoRenderer.triggerDiceRoll(roll)
  }

  setTimeout(() => {
    evaluateLudoTurnMoves(roll)
  }, 650)
}

function evaluateLudoTurnMoves(roll: number) {
  const player = ludoPlayers.value[ludoCurrentTurn.value]
  const validMoves: any[] = []

  // Check pieces
  player.pieces.forEach((p: any) => {
    if (p.state === 0 && roll === 6) {
      validMoves.push({ player: player.color, pieceId: p.id, type: 'Deploy' })
    } else if (p.state === 1 || p.state === 2) {
      if (p.step + roll <= 56) {
        validMoves.push({ player: player.color, pieceId: p.id, type: 'Move', steps: roll })
      }
    }
  })

  if (validMoves.length === 0) {
    // No moves available -> next player (unless rolled 6)
    if (roll !== 6) {
      ludoCurrentTurn.value = (ludoCurrentTurn.value + 1) % ludoPlayerCount.value
    }
    ludoCanRoll.value = true
    syncLudoRenderer([])

    checkBotTurn()
  } else if (validMoves.length === 1 && (!player.isHuman || validMoves[0].type === 'Deploy')) {
    // Auto execute single move or bot move
    executeLudoMove(validMoves[0])
  } else if (!player.isHuman) {
    // Bot chooses best move
    const chosen = validMoves[Math.floor(Math.random() * validMoves.length)]
    executeLudoMove(chosen)
  } else {
    // Wait for human to click piece
    syncLudoRenderer(validMoves)
  }
}

function handleLudoPieceSelected(pieceId: number) {
  const player = ludoPlayers.value[ludoCurrentTurn.value]
  const piece = player.pieces.find((p: any) => p.id === pieceId)
  if (!piece) return

  if (piece.state === 0 && ludoDiceRoll.value === 6) {
    executeLudoMove({ player: player.color, pieceId, type: 'Deploy' })
  } else if ((piece.state === 1 || piece.state === 2) && piece.step + ludoDiceRoll.value <= 56) {
    executeLudoMove({ player: player.color, pieceId, type: 'Move', steps: ludoDiceRoll.value })
  }
}

function executeLudoMove(move: any) {
  const player = ludoPlayers.value[move.player]
  const piece = player.pieces.find((p: any) => p.id === move.pieceId)
  if (!piece) return

  if (move.type === 'Deploy') {
    piece.state = 1
    piece.step = 0
  } else {
    piece.step += move.steps
    if (piece.step >= 56) {
      piece.state = 3 // Finished in center goal
    } else if (piece.step >= 50) {
      piece.state = 2 // Home track
    }
  }

  updateLudoPieceCoords()

  // Check kick opponent
  checkLudoKicks(player.color, piece)

  // Check winner
  if (player.pieces.every((p: any) => p.state === 3)) {
    ludoWinner.value = player.color
  } else {
    if (ludoDiceRoll.value !== 6) {
      ludoCurrentTurn.value = (ludoCurrentTurn.value + 1) % ludoPlayerCount.value
    }
    ludoCanRoll.value = true
  }

  syncLudoRenderer([])
  checkBotTurn()
}

function checkLudoKicks(myColor: number, myPiece: any) {
  if (myPiece.state !== 1) return
  const myTrackPos = myPiece.trackPos

  ludoPlayers.value.forEach((p) => {
    if (p.color === myColor) return
    p.pieces.forEach((other: any) => {
      if (other.state === 1 && other.trackPos === myTrackPos && other.step < 50) {
        // Kicked back to yard!
        other.state = 0
        other.step = -1
        other.trackPos = -1
        other.trackPosition = -1
        other.homeStep = -1
        other.homeTrackStep = -1
        if (ludoRenderer) {
          ludoRenderer.triggerKickFeathers(myTrackPos)
        }
      }
    })
  })
}

function checkBotTurn() {
  if (ludoWinner.value !== null) return
  const current = ludoPlayers.value[ludoCurrentTurn.value]
  if (!current.isHuman) {
    setTimeout(() => {
      handleLudoRollDice()
    }, 700)
  }
}

// ==========================================
// 3. SNAKE (ĐÀN GÀ CON) STATE & LOGIC
// ==========================================
const snakeScore = ref(0)
const snakeHighScore = ref(parseInt(localStorage.getItem('stp_snake_highscore') || '0', 10))
const snakeIsRunning = ref(false)
const snakeIsPaused = ref(false)
const snakeIsGameOver = ref(false)
const snakeWallWrap = ref(true)
const snakeSpeed = ref(130)

let snakeBody: Array<{ x: number; y: number }> = []
let snakeDir = { x: 1, y: 0 }
let nextSnakeDir = { x: 1, y: 0 }
let snakeFood = { x: 10, y: 10, type: 0 }
let snakeTimer: any = null

function initSnakeGame() {
  snakeBody = [
    { x: 10, y: 10 },
    { x: 9, y: 10 },
    { x: 8, y: 10 },
  ]
  snakeDir = { x: 1, y: 0 }
  nextSnakeDir = { x: 1, y: 0 }
  snakeScore.value = 0
  snakeIsGameOver.value = false
  snakeIsPaused.value = false
  snakeIsRunning.value = true
  snakeSpeed.value = 130
  spawnSnakeFood()
  syncSnakeRenderer()

  clearInterval(snakeTimer)
  snakeTimer = setInterval(stepSnake, snakeSpeed.value)
}

function spawnSnakeFood() {
  let x = Math.floor(Math.random() * 18) + 1
  let y = Math.floor(Math.random() * 18) + 1
  while (snakeBody.some(b => b.x === x && b.y === y)) {
    x = Math.floor(Math.random() * 18) + 1
    y = Math.floor(Math.random() * 18) + 1
  }
  const type = Math.floor(Math.random() * 4) // 0: Corn, 1: Carrot, 2: Beet, 3: Pumpkin
  snakeFood = { x, y, type }
}

function stepSnake() {
  if (!snakeIsRunning.value || snakeIsPaused.value || snakeIsGameOver.value) return

  snakeDir = { ...nextSnakeDir }
  const head = snakeBody[0]
  let nx = head.x + snakeDir.x
  let ny = head.y + snakeDir.y

  if (snakeWallWrap.value) {
    if (nx < 0) nx = 19
    if (nx > 19) nx = 0
    if (ny < 0) ny = 19
    if (ny > 19) ny = 0
  } else {
    if (nx < 0 || nx > 19 || ny < 0 || ny > 19) {
      triggerSnakeGameOver()
      return
    }
  }

  // Self bite
  if (snakeBody.some((b, i) => i > 0 && b.x === nx && b.y === ny)) {
    triggerSnakeGameOver()
    return
  }

  snakeBody.unshift({ x: nx, y: ny })

  // Eat food
  if (nx === snakeFood.x && ny === snakeFood.y) {
    snakeScore.value += 10
    if (snakeScore.value > snakeHighScore.value) {
      snakeHighScore.value = snakeScore.value
      localStorage.setItem('stp_snake_highscore', snakeScore.value.toString())
    }
    spawnSnakeFood()

    // Gradual acceleration
    if (snakeSpeed.value > 65) {
      snakeSpeed.value = Math.max(65, snakeSpeed.value - 2)
      clearInterval(snakeTimer)
      snakeTimer = setInterval(stepSnake, snakeSpeed.value)
    }
  } else {
    snakeBody.pop()
  }

  syncSnakeRenderer()
}

function triggerSnakeGameOver() {
  snakeIsGameOver.value = true
  snakeIsRunning.value = false
  clearInterval(snakeTimer)
  syncSnakeRenderer()
}

function setSnakeDirection(dir: 'Up' | 'Down' | 'Left' | 'Right') {
  if (dir === 'Up' && snakeDir.y === 0) nextSnakeDir = { x: 0, y: -1 }
  else if (dir === 'Down' && snakeDir.y === 0) nextSnakeDir = { x: 0, y: 1 }
  else if (dir === 'Left' && snakeDir.x === 0) nextSnakeDir = { x: -1, y: 0 }
  else if (dir === 'Right' && snakeDir.x === 0) nextSnakeDir = { x: 1, y: 0 }
}

function syncSnakeRenderer() {
  if (!snakeRenderer) return
  let dirName = 'Right'
  if (snakeDir.x === -1) dirName = 'Left'
  else if (snakeDir.y === -1) dirName = 'Up'
  else if (snakeDir.y === 1) dirName = 'Down'

  snakeRenderer.updateState({
    head: snakeBody[0] || { x: 10, y: 10 },
    body: snakeBody.slice(1),
    food: snakeFood,
    score: snakeScore.value,
    highScore: snakeHighScore.value,
    isRunning: snakeIsRunning.value,
    isPaused: snakeIsPaused.value,
    isGameOver: snakeIsGameOver.value,
    wallWrap: snakeWallWrap.value,
    direction: dirName.toLowerCase(),
  })
}

// ==========================================
// 4. GAME 2048 (GỘP NÔNG SẢN) STATE & LOGIC
// ==========================================
const g2048Grid = ref<number[][]>([
  [0, 0, 0, 0],
  [0, 0, 0, 0],
  [0, 0, 0, 0],
  [0, 0, 0, 0],
])
const g2048Score = ref(0)
const g2048HighScore = ref(parseInt(localStorage.getItem('stp_2048_highscore') || '0', 10))
const g2048IsGameOver = ref(false)
const g2048History = ref<Array<{ grid: number[][]; score: number }>>([])

const cropTiers = [
  { val: 2, name: 'Hạt mầm', icon: '🌱' },
  { val: 4, name: 'Cải mầm', icon: '🌿' },
  { val: 8, name: 'Lúa mì', icon: '🌾' },
  { val: 16, name: 'Cà rốt', icon: '🥕' },
  { val: 32, name: 'Cà chua', icon: '🍅' },
  { val: 64, name: 'Bắp ngô', icon: '🌽' },
  { val: 128, name: 'Cà tím', icon: '🍆' },
  { val: 256, name: 'Dưa hấu', icon: '🍉' },
  { val: 512, name: 'Dâu tây', icon: '🍓' },
  { val: 1024, name: 'Bí đỏ', icon: '🎃' },
  { val: 2048, name: 'Bí Ngô Vàng', icon: '✨' },
]

function init2048Game() {
  g2048Grid.value = [
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
  ]
  g2048Score.value = 0
  g2048IsGameOver.value = false
  g2048History.value = []
  spawn2048Tile()
  spawn2048Tile()
  sync2048Renderer()
}

function spawn2048Tile() {
  const empty: Array<[number, number]> = []
  for (let r = 0; r < 4; r++) {
    for (let c = 0; c < 4; c++) {
      if (g2048Grid.value[r][c] === 0) empty.push([r, c])
    }
  }
  if (empty.length === 0) return null
  const [r, c] = empty[Math.floor(Math.random() * empty.length)]
  g2048Grid.value[r][c] = Math.random() < 0.9 ? 2 : 4
  return { r, c, val: g2048Grid.value[r][c] }
}

function move2048(direction: 'up' | 'down' | 'left' | 'right') {
  if (g2048IsGameOver.value) return

  // Save state for undo
  const prevGrid = g2048Grid.value.map(row => [...row])
  const prevScore = g2048Score.value

  let rotated = g2048Grid.value.map(row => [...row])
  if (direction === 'up') rotated = rotateLeft(rotated)
  else if (direction === 'right') rotated = rotate180(rotated)
  else if (direction === 'down') rotated = rotateRight(rotated)

  const mergedTiles: Array<{ r: number; c: number }> = []
  let hasMoved = false

  for (let r = 0; r < 4; r++) {
    const row = rotated[r].filter(v => v !== 0)
    for (let c = 0; c < row.length - 1; c++) {
      if (row[c] === row[c + 1]) {
        row[c] *= 2
        g2048Score.value += row[c]
        row.splice(c + 1, 1)
        mergedTiles.push({ r, c })
      }
    }
    while (row.length < 4) row.push(0)
    for (let c = 0; c < 4; c++) {
      if (rotated[r][c] !== row[c]) hasMoved = true
      rotated[r][c] = row[c]
    }
  }

  // Rotate back
  if (direction === 'up') rotated = rotateRight(rotated)
  else if (direction === 'right') rotated = rotate180(rotated)
  else if (direction === 'down') rotated = rotateLeft(rotated)

  if (hasMoved) {
    g2048History.value.push({ grid: prevGrid, score: prevScore })
    if (g2048History.value.length > 5) g2048History.value.shift()

    g2048Grid.value = rotated
    const spawned = spawn2048Tile()

    if (g2048Score.value > g2048HighScore.value) {
      g2048HighScore.value = g2048Score.value
      localStorage.setItem('stp_2048_highscore', g2048Score.value.toString())
    }

    check2048GameOver()
    sync2048Renderer(mergedTiles, spawned)
  }
}

function rotateLeft(grid: number[][]): number[][] {
  const res = Array(4).fill(0).map(() => Array(4).fill(0))
  for (let r = 0; r < 4; r++) {
    for (let c = 0; c < 4; c++) {
      res[3 - c][r] = grid[r][c]
    }
  }
  return res
}

function rotateRight(grid: number[][]): number[][] {
  const res = Array(4).fill(0).map(() => Array(4).fill(0))
  for (let r = 0; r < 4; r++) {
    for (let c = 0; c < 4; c++) {
      res[c][3 - r] = grid[r][c]
    }
  }
  return res
}

function rotate180(grid: number[][]): number[][] {
  return rotateRight(rotateRight(grid))
}

function undo2048() {
  if (g2048History.value.length === 0) return
  const prev = g2048History.value.pop()!
  g2048Grid.value = prev.grid
  g2048Score.value = prev.score
  g2048IsGameOver.value = false
  sync2048Renderer()
}

function check2048GameOver() {
  for (let r = 0; r < 4; r++) {
    for (let c = 0; c < 4; c++) {
      if (g2048Grid.value[r][c] === 0) return
      if (c < 3 && g2048Grid.value[r][c] === g2048Grid.value[r][c + 1]) return
      if (r < 3 && g2048Grid.value[r][c] === g2048Grid.value[r + 1][c]) return
    }
  }
  g2048IsGameOver.value = true
}

function sync2048Renderer(merged: any[] = [], spawned: any = null) {
  if (!game2048Renderer) return
  const tierBoard = g2048Grid.value.map(row =>
    row.map(val => (val > 0 ? Math.min(11, Math.round(Math.log2(val))) : 0))
  )
  game2048Renderer.updateState({
    grid: g2048Grid.value,
    board: g2048Grid.value,
    tierBoard,
    score: g2048Score.value,
    highScore: g2048HighScore.value,
    isGameOver: g2048IsGameOver.value,
    lastMerged: merged,
    lastSpawned: spawned,
  })
}

// ==========================================
// RENDERER LIFECYCLE & ROUTING
// ==========================================
const loadModule = (url: string): Promise<any> => {
  return (new Function('u', 'return import(u)'))(url)
}

async function mountActiveRenderer() {
  await nextTick()
  if (!gameCanvas.value) return

  // Dynamically import ES modules from /js/games/
  try {
    const sfxMod = await loadModule('/js/games/shared/sfx.js')
    sfxModule = sfxMod.sfx
    sfxModule.setMuted(isMuted.value)
  } catch (err) {
    console.warn('Load shared game modules error:', err)
  }

  const slug = activeGame.value

  if (slug === 'caro') {
    const mod = await loadModule('/js/games/caro.renderer.js')
    caroRenderer = mod.initCaro(gameCanvas.value, {
      onCellClicked: (r: number, c: number) => handleCaroCellClick(r, c),
    })
    initCaroGame()
  } else if (slug === 'co-ca-ngua') {
    const mod = await loadModule('/js/games/ludo.renderer.js')
    ludoRenderer = mod.initLudo(gameCanvas.value, {
      onRollDiceClicked: () => handleLudoRollDice(),
      onSelectMove: (p: number) => handleLudoPieceSelected(p),
    })
    initLudoGame()
  } else if (slug === 'dan-ga-con') {
    const mod = await loadModule('/js/games/snake.renderer.js')
    snakeRenderer = mod.initSnake(gameCanvas.value, {
      onStartOrRestart: () => initSnakeGame(),
      onDirectionChange: (dir: string) => setSnakeDirection(dir as any),
    })
    initSnakeGame()
  } else if (slug === 'gop-nong-san') {
    const mod = await loadModule('/js/games/game2048.renderer.js')
    game2048Renderer = mod.initGame2048(gameCanvas.value, {
      onMove: (dir: 'up' | 'down' | 'left' | 'right') => move2048(dir),
    })
    init2048Game()
  }
}

function destroyRenderers() {
  clearInterval(snakeTimer)
  if (caroRenderer) { caroRenderer.destroy(); caroRenderer = null }
  if (ludoRenderer) { ludoRenderer.destroy(); ludoRenderer = null }
  if (snakeRenderer) { snakeRenderer.destroy(); snakeRenderer = null }
  if (game2048Renderer) { game2048Renderer.destroy(); game2048Renderer = null }
}

watch(activeGame, () => {
  destroyRenderers()
  if (activeGame.value) {
    mountActiveRenderer()
  }
})

// ==========================================
// GLOBAL SHORTCUTS & OFFICE SAFETY (BOSS KEY)
// ==========================================
function toggleSound() {
  isMuted.value = !isMuted.value
  if (sfxModule) {
    sfxModule.setMuted(isMuted.value)
  }
}

function handleKeyDown(e: KeyboardEvent) {
  // Boss key: Esc
  if (e.key === 'Escape') {
    e.preventDefault()
    isBossScreen.value = !isBossScreen.value
    return
  }

  if (isBossScreen.value) return

  // Snake controls
  if (activeGame.value === 'dan-ga-con') {
    if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W') {
      if (snakeDir.y === 0) nextSnakeDir = { x: 0, y: -1 }
    } else if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S') {
      if (snakeDir.y === 0) nextSnakeDir = { x: 0, y: 1 }
    } else if (e.key === 'ArrowLeft' || e.key === 'a' || e.key === 'A') {
      if (snakeDir.x === 0) nextSnakeDir = { x: -1, y: 0 }
    } else if (e.key === 'ArrowRight' || e.key === 'd' || e.key === 'D') {
      if (snakeDir.x === 0) nextSnakeDir = { x: 1, y: 0 }
    } else if (e.key === ' ' || e.key === 'Spacebar') {
      if (!snakeIsRunning.value || snakeIsGameOver.value) {
        initSnakeGame()
      }
    } else if (e.key === 'p' || e.key === 'P') {
      snakeIsPaused.value = !snakeIsPaused.value
      syncSnakeRenderer()
    }
  }

  // 2048 controls
  if (activeGame.value === 'gop-nong-san') {
    if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W') {
      e.preventDefault(); move2048('up')
    } else if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S') {
      e.preventDefault(); move2048('down')
    } else if (e.key === 'ArrowLeft' || e.key === 'a' || e.key === 'A') {
      e.preventDefault(); move2048('left')
    } else if (e.key === 'ArrowRight' || e.key === 'd' || e.key === 'D') {
      e.preventDefault(); move2048('right')
    } else if (e.key === 'z' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault(); undo2048()
    }
  }
}

function handleWindowBlur() {
  // Auto pause active game when focus is lost
  if (activeGame.value === 'dan-ga-con' && snakeIsRunning.value) {
    snakeIsPaused.value = true
    syncSnakeRenderer()
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleKeyDown)
  window.addEventListener('blur', handleWindowBlur)
  if (activeGame.value) {
    mountActiveRenderer()
  }
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeyDown)
  window.removeEventListener('blur', handleWindowBlur)
  destroyRenderers()
})
</script>

<template>
  <div class="games-container min-h-[calc(100vh-80px)] p-4 md:p-6 bg-[#2c1d11] text-[#fff8eb] font-sans selection:bg-[#d3a364] selection:text-[#2c1d11]">
    <!-- Top Bar / Office Safety Tools -->
    <header class="flex flex-wrap items-center justify-between gap-3 pb-4 mb-6 border-b border-[#613b19]">
      <div class="flex items-center gap-3">
        <router-link
          v-if="activeGame"
          to="/giai-tri"
          class="pixel-btn pixel-btn-wood px-3 py-1.5 text-xs flex items-center gap-1.5 font-pixel"
        >
          <ArrowLeft class="w-4 h-4" /> Về Nông Trại
        </router-link>
        <div>
          <h1 class="font-pixel text-xl md:text-2xl font-bold text-[#fbc531] flex items-center gap-2">
            <span>🌾</span> Nông Trại Thư Giãn
            <span class="text-xs px-2 py-0.5 bg-[#4e2b14] border border-[#d3a364] text-[#78c850] rounded font-mono font-normal">
              Stardew Edition
            </span>
          </h1>
          <p class="text-xs text-[#ceb38b] hidden sm:block">
            Giải trí ấm áp giữa giờ làm việc · Đồ họa pixel ấm · Tiết kiệm tài nguyên
          </p>
        </div>
      </div>

      <div class="flex items-center gap-2 font-pixel">
        <!-- Sound Mute Toggle -->
        <button
          @click="toggleSound"
          class="pixel-btn pixel-btn-wood px-3 py-1.5 text-xs flex items-center gap-1.5"
          :title="isMuted ? 'Bật âm thanh chiptune' : 'Tắt âm thanh (Mute)'"
        >
          <component :is="isMuted ? VolumeX : Volume2" class="w-4 h-4 text-[#fbc531]" />
          <span>{{ isMuted ? 'Âm thanh: Tắt' : 'Âm thanh: Bật' }}</span>
        </button>

        <!-- Boss Key (Esc) -->
        <button
          @click="isBossScreen = true"
          class="pixel-btn pixel-btn-red px-3 py-1.5 text-xs flex items-center gap-1.5 font-bold"
          title="Bấm phím Esc bất cứ lúc nào để lập tức bật bảng dữ liệu công việc ngụy trang!"
        >
          <EyeOff class="w-4 h-4" />
          <span>Ẩn nhanh (Esc)</span>
        </button>
      </div>
    </header>

    <!-- ============================================== -->
    <!-- MAIN HUB: 4 GAME CARDS SELECTION               -->
    <!-- ============================================== -->
    <div v-if="!activeGame" class="max-w-5xl mx-auto space-y-6">
      <div class="pixel-box-wood p-5 rounded-lg">
        <div class="flex flex-wrap items-center justify-between gap-4">
          <div class="space-y-1">
            <h2 class="font-pixel text-lg font-bold text-[#ffeb99] flex items-center gap-2">
              <Sparkles class="w-5 h-5 text-[#fbc531]" /> Chào mừng bạn đến Góc Thư Giãn STP
            </h2>
            <p class="text-xs text-[#f5ecd5] max-w-2xl leading-relaxed">
              Chọn 1 trong 4 mini-game pixel art ấm áp phong cách nông trại để nạp lại năng lượng sau những giờ nhập liệu và đối soát căng thẳng.
            </p>
          </div>
          <div class="flex items-center gap-3 bg-[#3a200f] px-3.5 py-2 rounded border border-[#613b19] text-xs font-pixel">
            <div class="w-2.5 h-2.5 rounded-full bg-[#78c850] animate-pulse"></div>
            <span>Máy chủ trực tuyến</span>
            <span class="text-[#ceb38b]">· Phím tắt: [Esc] ngụy trang</span>
          </div>
        </div>
      </div>

      <!-- 4 Game Cards Grid -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div
          v-for="game in games"
          :key="game.slug"
          class="pixel-box-parchment p-5 rounded flex flex-col justify-between hover:translate-y-[-2px] transition duration-150 group"
        >
          <div>
            <div class="flex items-start justify-between gap-3 mb-3">
              <div class="flex items-center gap-3">
                <span class="text-3xl p-2 bg-[#fdfaf2] rounded border-2 border-[#b38449] shadow-sm">
                  {{ game.icon }}
                </span>
                <div>
                  <h3 class="font-pixel text-lg font-bold text-[#2e170a] group-hover:text-[#ab7338] transition">
                    {{ game.title }}
                  </h3>
                  <p class="text-xs font-semibold text-[#8b5a2b]">{{ game.subtitle }}</p>
                </div>
              </div>
              <span class="font-pixel text-[11px] px-2 py-0.5 rounded bg-[#e8d5b5] text-[#4a2810] font-bold border border-[#b38449]">
                {{ game.tag }}
              </span>
            </div>

            <p class="text-xs text-[#4a2810] leading-relaxed mb-4">
              {{ game.description }}
            </p>
          </div>

          <div class="pt-3 border-t border-[#ceb38b] flex items-center justify-between">
            <span class="text-xs text-[#6e461f] font-pixel flex items-center gap-1.5">
              <Users class="w-3.5 h-3.5" /> {{ game.players }}
            </span>
            <router-link
              :to="`/giai-tri/${game.slug}`"
              class="pixel-btn pixel-btn-green px-4 py-2 text-xs font-pixel flex items-center gap-1.5"
            >
              <Play class="w-3.5 h-3.5" /> Chơi Ngay
            </router-link>
          </div>
        </div>
      </div>
    </div>

    <!-- ============================================== -->
    <!-- PLAY ARENA VIEW                                -->
    <!-- ============================================== -->
    <div v-else class="max-w-4xl mx-auto flex flex-col items-center">
      <!-- Game Sub-header & Quick Settings -->
      <div class="w-full mb-4 flex flex-wrap items-center justify-between gap-3 pixel-box-soil p-3 rounded">
        <div class="flex items-center gap-3">
          <span class="text-2xl">{{ games.find(g => g.slug === activeGame)?.icon }}</span>
          <div>
            <h2 class="font-pixel text-base font-bold text-[#ffeb99]">
              {{ games.find(g => g.slug === activeGame)?.title }}
            </h2>
            <p class="text-[11px] text-[#ceb38b]">
              {{ games.find(g => g.slug === activeGame)?.subtitle }}
            </p>
          </div>
        </div>

        <!-- Mode Specific Action Buttons -->
        <div class="flex items-center gap-2 font-pixel text-xs">
          <!-- Caro Controls -->
          <template v-if="activeGame === 'caro'">
            <div class="flex items-center gap-1 bg-[#3a200f] p-1 rounded border border-[#613b19]">
              <button
                @click="caroMode = 'bot'; initCaroGame()"
                :class="caroMode === 'bot' ? 'pixel-btn-amber text-black' : 'text-[#ceb38b]'"
                class="px-2 py-0.5 rounded font-pixel"
              >
                Đấu Máy
              </button>
              <button
                @click="caroMode = 'pvp'; initCaroGame()"
                :class="caroMode === 'pvp' ? 'pixel-btn-amber text-black' : 'text-[#ceb38b]'"
                class="px-2 py-0.5 rounded font-pixel"
              >
                2 Người
              </button>
            </div>
            <button @click="initCaroGame" class="pixel-btn pixel-btn-wood px-2.5 py-1">
              <RotateCcw class="w-3.5 h-3.5 mr-1 inline" /> Ván mới
            </button>
          </template>

          <!-- Ludo Controls -->
          <template v-if="activeGame === 'co-ca-ngua'">
            <button
              @click="handleLudoRollDice"
              :disabled="!ludoCanRoll || ludoWinner !== null"
              class="pixel-btn pixel-btn-green px-3 py-1 font-bold"
            >
              🎲 Lắc Xúc Xắc
            </button>
            <button @click="initLudoGame" class="pixel-btn pixel-btn-wood px-2.5 py-1">
              <RotateCcw class="w-3.5 h-3.5 mr-1 inline" /> Bắt đầu lại
            </button>
          </template>

          <!-- Snake Controls -->
          <template v-if="activeGame === 'dan-ga-con'">
            <div class="flex items-center gap-2 mr-2">
              <span class="text-[#fbc531]">Điểm: {{ snakeScore }}</span>
              <span class="text-[#ceb38b]">| Kỷ lục: {{ snakeHighScore }}</span>
            </div>
            <button
              @click="snakeWallWrap = !snakeWallWrap; syncSnakeRenderer()"
              class="pixel-btn pixel-btn-wood px-2 py-1 text-[11px]"
            >
              {{ snakeWallWrap ? 'Xuyên rào: BẬT' : 'Xuyên rào: TẮT' }}
            </button>
            <button @click="initSnakeGame" class="pixel-btn pixel-btn-green px-2.5 py-1">
              {{ snakeIsGameOver ? 'Chơi lại' : 'Ván mới' }}
            </button>
          </template>

          <!-- 2048 Controls -->
          <template v-if="activeGame === 'gop-nong-san'">
            <div class="flex items-center gap-2 mr-2">
              <span class="text-[#fbc531]">Điểm: {{ g2048Score }}</span>
              <span class="text-[#ceb38b]">| Kỷ lục: {{ g2048HighScore }}</span>
            </div>
            <button
              @click="undo2048"
              :disabled="g2048History.length === 0"
              class="pixel-btn pixel-btn-wood px-2.5 py-1 disabled:opacity-50"
            >
              <RotateCcw class="w-3.5 h-3.5 mr-1 inline" /> Đi lại (Z)
            </button>
            <button @click="isBookOpen = true" class="pixel-btn pixel-btn-amber px-2 py-1 text-[11px]">
              <BookOpen class="w-3.5 h-3.5 mr-1 inline" /> Sưu tập
            </button>
            <button @click="init2048Game" class="pixel-btn pixel-btn-green px-2.5 py-1">
              Ván mới
            </button>
          </template>
        </div>
      </div>

      <!-- Canvas Box Wrapper -->
      <div class="pixel-box-wood p-3 rounded-lg shadow-2xl relative flex items-center justify-center max-w-[480px] w-full">
        <canvas
          ref="gameCanvas"
          class="pixel-canvas cursor-pointer block max-w-full rounded border-2 border-[#4a2810]"
          style="width: min(88vw, 440px); height: auto; aspect-ratio: 1/1;"
        ></canvas>
      </div>

      <!-- ON-SCREEN CONTROLS & STATUS -->
      <!-- Caro Status & Interactive Info -->
      <div v-if="activeGame === 'caro'" class="mt-3 w-full max-w-[480px] bg-[#3a200f] p-2.5 rounded border border-[#613b19] flex items-center justify-between text-xs font-pixel">
        <div v-if="caroWinner" class="w-full text-center text-sm font-bold text-[#fbc531]">
          {{ caroWinner === 'Draw' ? '🤝 Hai bên hòa cờ!' : (caroWinner === 'X' ? '🌻 Bạn (Hoa Hướng Dương) Chiến Thắng!' : '🥕 Máy (Cà Rốt) Chiến Thắng!') }}
        </div>
        <template v-else>
          <div class="flex items-center gap-2">
            <span class="text-[#ceb38b]">Lượt:</span>
            <span :class="caroTurn === 'X' ? 'text-[#ffcc00] font-bold' : 'text-[#f36e21] font-bold'">
              {{ caroTurn === 'X' ? '🌻 Hoa Hướng Dương (X - Bạn)' : (caroMode === 'bot' ? '🥕 Cà Rốt (O - Máy)' : '🥕 Cà Rốt (O - Người 2)') }}
            </span>
          </div>
          <div v-if="caroIsAiThinking" class="text-[#fbc531] animate-pulse">
            🐥 Máy đang suy nghĩ...
          </div>
          <div v-else class="text-[11px] text-[#ceb38b]">
            Nước đi #{{ caroMoveCount }}
          </div>
        </template>
      </div>

      <!-- Ludo Interactive Controls -->
      <div v-if="activeGame === 'co-ca-ngua'" class="mt-3 w-full max-w-[480px] bg-[#3a200f] p-3 rounded border border-[#613b19] flex flex-col items-center gap-2 text-xs font-pixel">
        <div v-if="ludoWinner !== null" class="text-sm font-bold text-[#fbc531]">
          🏆 Người chơi {{ ['Đỏ', 'Lam', 'Vàng', 'Lục'][ludoWinner] }} đã về đích toàn bộ đàn gà!
        </div>
        <div v-else class="flex flex-wrap items-center justify-between w-full gap-2">
          <div class="flex items-center gap-2">
            <span class="text-[#ceb38b]">Lượt của:</span>
            <span class="font-bold text-sm" :style="{ color: ['#e74c3c', '#3498db', '#f1c40f', '#2ecc71'][ludoCurrentTurn] }">
              {{ ['Nông dân Đỏ (Bạn)', 'Nông dân Lam (Bot)', 'Nông dân Vàng (Bot)', 'Nông dân Lục (Bot)'][ludoCurrentTurn] }}
            </span>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-[#ceb38b]">Điểm xúc xắc:</span>
            <span class="px-2 py-0.5 bg-[#4e2b14] border border-[#d3a364] text-[#fbc531] font-bold text-sm rounded">
              {{ ludoDiceRoll > 0 ? ludoDiceRoll : '-' }}
            </span>
          </div>
        </div>

        <button
          v-if="ludoCanRoll && ludoWinner === null && ludoPlayers[ludoCurrentTurn]?.isHuman"
          @click="handleLudoRollDice"
          class="pixel-btn pixel-btn-green w-full py-2 text-sm font-bold shadow-lg animate-bounce mt-1"
        >
          🎲 BẤM ĐỂ LẮC XÚC XẮC NGAY
        </button>
      </div>

      <!-- Snake Virtual D-Pad -->
      <div v-if="activeGame === 'dan-ga-con'" class="mt-3 flex flex-col items-center gap-1.5 font-pixel select-none">
        <button @click="setSnakeDirection('Up')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">▲</button>
        <div class="flex items-center gap-3">
          <button @click="setSnakeDirection('Left')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">◀</button>
          <button
            @click="snakeIsPaused = !snakeIsPaused; syncSnakeRenderer()"
            class="pixel-btn pixel-btn-amber px-3 py-1.5 text-xs font-bold"
          >
            {{ snakeIsPaused ? '▶ Tiếp tục' : '⏸ Tạm dừng' }}
          </button>
          <button @click="setSnakeDirection('Right')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">▶</button>
        </div>
        <button @click="setSnakeDirection('Down')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">▼</button>
      </div>

      <!-- 2048 Virtual D-Pad -->
      <div v-if="activeGame === 'gop-nong-san'" class="mt-3 flex flex-col items-center gap-1.5 font-pixel select-none">
        <button @click="move2048('up')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">▲</button>
        <div class="flex items-center gap-3">
          <button @click="move2048('left')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">◀</button>
          <button
            @click="undo2048"
            :disabled="g2048History.length === 0"
            class="pixel-btn pixel-btn-amber px-3 py-1.5 text-xs font-bold disabled:opacity-50"
          >
            ↩ Đi lại
          </button>
          <button @click="move2048('right')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">▶</button>
        </div>
        <button @click="move2048('down')" class="pixel-btn pixel-btn-wood w-14 h-10 flex items-center justify-center text-lg active:scale-95 shadow font-bold">▼</button>
      </div>

      <!-- Quick Guidance Footer -->
      <div class="mt-4 text-center text-xs text-[#ceb38b] font-pixel space-y-1">
        <p v-if="activeGame === 'caro'">
          🌻 Bạn cầm Hoa Hướng Dương (X) đi trước. Bấm vào ô đất để gieo hạt. Thẳng hàng 5 cây để giành chiến thắng!
        </p>
        <p v-if="activeGame === 'co-ca-ngua'">
          🎲 Lắc được 6 điểm để xuất chuồng hoặc nhận thêm 1 lượt tung! Bấm vào chú gà có vòng vàng nhấp nháy để di chuyển.
        </p>
        <p v-if="activeGame === 'dan-ga-con'">
          🐥 Dùng phím [Mũi tên] hoặc [W A S D] để điều khiển mẹ gà. Bấm [Phím Cách] để bắt đầu/chơi lại, [P] tạm dừng.
        </p>
        <p v-if="activeGame === 'gop-nong-san'">
          🌽 Dùng các phím [Mũi tên] để dồn các nông sản giống nhau lại để nâng cấp lên bậc cao hơn!
        </p>
        <p class="text-[11px] text-[#8b5a2b]">
          Tip bảo vệ văn phòng: Nhấn phím <span class="text-[#fbc531] font-bold">[Esc]</span> hoặc nút Ẩn nhanh bất cứ lúc nào!
        </p>
      </div>
    </div>

    <!-- ============================================== -->
    <!-- COLLECTION BOOK MODAL (2048 CROP TIERS)        -->
    <!-- ============================================== -->
    <div
      v-if="isBookOpen"
      class="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-4 backdrop-blur-sm"
      @click.self="isBookOpen = false"
    >
      <div class="pixel-box-parchment max-w-lg w-full p-5 rounded-lg text-[#2e170a] font-pixel shadow-2xl relative">
        <button
          @click="isBookOpen = false"
          class="absolute top-3 right-3 text-[#613b19] hover:text-[#ab7338]"
        >
          <X class="w-5 h-5" />
        </button>

        <h3 class="text-base font-bold text-[#8b5a2b] flex items-center gap-2 mb-3">
          <BookOpen class="w-5 h-5 text-[#d3a364]" /> Sách Sưu Tập Nông Sản Nông Trại
        </h3>
        <p class="text-xs text-[#4a2810] mb-4">
          Ghép 2 loại nông sản cùng cấp để tiến hóa lên bậc nông sản cao hơn!
        </p>

        <div class="grid grid-cols-2 sm:grid-cols-3 gap-2.5 text-xs">
          <div
            v-for="tier in cropTiers"
            :key="tier.val"
            class="p-2.5 rounded bg-[#f5ecd5] border border-[#d3a364] flex items-center gap-2"
          >
            <span class="text-2xl">{{ tier.icon }}</span>
            <div>
              <div class="font-bold text-[#2e170a]">{{ tier.name }}</div>
              <div class="text-[11px] text-[#8b5a2b]">Giá trị: {{ tier.val }}</div>
            </div>
          </div>
        </div>

        <div class="mt-4 pt-3 border-t border-[#d3a364] text-center">
          <button @click="isBookOpen = false" class="pixel-btn pixel-btn-wood px-4 py-1.5 text-xs">
            Đóng Sách
          </button>
        </div>
      </div>
    </div>

    <!-- ============================================== -->
    <!-- BOSS SCREEN (FAUX EXCEL SPREADSHEET)           -->
    <!-- ============================================== -->
    <div
      v-if="isBossScreen"
      class="fixed inset-0 z-[9999] bg-[#f0f4f9] text-[#1e293b] font-sans flex flex-col select-none"
      @click="isBossScreen = false"
    >
      <!-- Faux Excel Ribbon Bar -->
      <div class="bg-[#107c41] text-white px-3 py-1.5 flex items-center justify-between text-xs">
        <div class="flex items-center gap-4">
          <span class="font-bold flex items-center gap-1.5">
            📊 STP Archive - Báo cáo Tổng hợp Hồ sơ Tư pháp 2026.xlsx [Chỉ đọc]
          </span>
          <span class="hidden md:inline bg-[#0b5a2f] px-2 py-0.5 rounded text-[11px]">Đang đồng bộ</span>
        </div>
        <div class="text-[11px] text-[#d1e7dd] flex items-center gap-2">
          <span>Bấm bất kỳ đâu hoặc phím [Esc] để quay lại</span>
        </div>
      </div>

      <!-- Faux Formula Bar -->
      <div class="bg-white border-b border-[#cbd5e1] px-3 py-1 flex items-center gap-2 text-xs">
        <span class="font-mono text-gray-400 font-bold">D12</span>
        <span class="text-gray-300">|</span>
        <span class="font-mono text-[#0f172a]">=VLOOKUP(B12, 'Dữ liệu gốc'!$A$2:$G$1450, 4, FALSE)</span>
      </div>

      <!-- Faux Spreadsheet Grid -->
      <div class="flex-1 overflow-auto bg-white">
        <table class="w-full text-xs text-left border-collapse border border-[#cbd5e1]">
          <thead class="bg-[#f8fafc] text-gray-500 font-mono text-[11px] border-b border-[#cbd5e1]">
            <tr>
              <th class="w-10 px-2 py-1 text-center border-r border-[#cbd5e1] bg-[#e2e8f0]"></th>
              <th class="px-3 py-1 border-r border-[#cbd5e1]">A (Hộp số)</th>
              <th class="px-3 py-1 border-r border-[#cbd5e1]">B (Hồ sơ số)</th>
              <th class="px-3 py-1 border-r border-[#cbd5e1]">C (Năm)</th>
              <th class="px-3 py-1 border-r border-[#cbd5e1]">D (Trích yếu nội dung)</th>
              <th class="px-3 py-1 border-r border-[#cbd5e1]">E (Số tờ)</th>
              <th class="px-3 py-1">F (Ghi chú lưu trữ)</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-[#e2e8f0] font-mono text-xs">
            <tr v-for="i in 25" :key="i" :class="i === 12 ? 'bg-[#e0f2fe]' : 'hover:bg-gray-50'">
              <td class="px-2 py-1 text-center border-r border-[#cbd5e1] bg-[#f8fafc] text-gray-400 font-bold">
                {{ i }}
              </td>
              <td class="px-3 py-1 border-r border-[#cbd5e1]">HỘP-{{ 100 + i }}</td>
              <td class="px-3 py-1 border-r border-[#cbd5e1]">{{ 8200 + i }}</td>
              <td class="px-3 py-1 border-r border-[#cbd5e1]">1994</td>
              <td class="px-3 py-1 border-r border-[#cbd5e1] font-sans">
                Về việc đăng ký kết hôn của Bà NGUYỄN THỊ {{ String.fromCharCode(65 + (i % 20)) }} và Ông TRẦN VĂN {{ String.fromCharCode(70 + (i % 15)) }} (giấy khai sinh, CMND, hộ khẩu, tờ khai)
              </td>
              <td class="px-3 py-1 border-r border-[#cbd5e1] text-right">01-{{ 20 + (i % 15) }}</td>
              <td class="px-3 py-1 text-gray-500 font-sans">Đã đối soát hoàn tất</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Faux Status Bar -->
      <div class="bg-[#f1f5f9] border-t border-[#cbd5e1] px-3 py-1 flex items-center justify-between text-[11px] text-gray-500 font-mono">
        <div class="flex items-center gap-3">
          <span>Sheet: ML_TongHop</span>
          <span>Dòng: 25 / 1.450</span>
        </div>
        <div>
          <span>SUM: 205.410 | AVERAGE: 8.216</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Scoped adjustments for smooth gameplay container */
.games-container {
  font-feature-settings: 'cv02', 'cv03', 'cv04', 'cv11';
}
</style>

