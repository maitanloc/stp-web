// Stardew-inspired Procedural Pixel-Art Sprite Atlas
// Palette: 32-48 warm colors, 1px dark borders, 3-tone shading, soft bottom shadows.
// All sprites are generated on 16x16 or 16x32 offscreen canvases for integer pixel-art rendering.

const PALETTE = {
  // Outline & Darks
  dark_outline: '#2c1810',
  dark_navy: '#1b2438',
  shadow_soft: 'rgba(28, 18, 12, 0.28)',

  // Grass & Greens
  grass_light: '#78c657',
  grass_mid: '#5c9e3d',
  grass_dark: '#3f6d28',
  leaf_light: '#88d75e',
  leaf_mid: '#4d9230',
  leaf_dark: '#2d5c1b',

  // Soil & Earth
  soil_light: '#b97a48',
  soil_mid: '#8b5028',
  soil_dark: '#583015',
  dirt_path: '#cfa26d',

  // Wood & Parchment
  wood_light: '#c88e50',
  wood_mid: '#9b642e',
  wood_dark: '#5c3917',
  parchment_light: '#f5ecd5',
  parchment_mid: '#dfcfab',
  parchment_dark: '#b5a17a',

  // Crops & Colors
  sunflower_yellow: '#ffcc00',
  sunflower_gold: '#e69900',
  sunflower_center: '#5c3317',
  carrot_orange: '#f36e21',
  carrot_deep: '#cc4d10',
  strawberry_red: '#e73845',
  strawberry_dark: '#a81c26',
  tomato_red: '#d83832',
  potato_tan: '#cf9d63',
  potato_dark: '#936636',
  cabbage_green: '#57b960',
  cabbage_light: '#86db8d',
  corn_yellow: '#f8d436',
  corn_green: '#539934',
  watermelon_green: '#2d7c3b',
  watermelon_light: '#65be5d',
  watermelon_red: '#e24141',
  pumpkin_orange: '#eb6c1e',
  pumpkin_dark: '#aa420c',
  pumpkin_gold: '#ffd124',
  pumpkin_glow: '#fff394',

  // Chickens & Animals
  feather_white: '#ffffff',
  feather_cream: '#fbe9d0',
  feather_shade: '#d6bfa3',
  comb_red: '#dc3545',
  beak_orange: '#f59e0b',
  chicken_red: '#d94b4b',
  chicken_blue: '#4b7cd9',
  chicken_green: '#4ab461',
  chicken_yellow: '#f3b432',

  // UI & Accents
  white: '#ffffff',
  black: '#111111',
  water_sparkle: '#a6e3e9',
  water_mid: '#4ba3e3',
  water_deep: '#2361a6',
};

class SpriteAtlas {
  constructor() {
    this.cache = new Map();
    this.isReady = false;
  }

  async init() {
    // Generate all procedural sprites into cache
    this.generateAll();
    this.isReady = true;
    return this;
  }

  get(name) {
    return this.cache.get(name) || null;
  }

  createCanvas(w = 16, h = 16) {
    const c = document.createElement('canvas');
    c.width = w;
    c.height = h;
    const ctx = c.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    return { canvas: c, ctx };
  }

  generateAll() {
    this.generateTiles();
    this.generateCaroSprites();
    this.generateLudoSprites();
    this.generateSnakeSprites();
    this.generate2048Sprites();
    this.generateEmojiSprites();
  }

  // 1. Common Farm Tiles
  generateTiles() {
    // Grass 1 (plain soft grass with subtle blades)
    const g1 = this.createCanvas(16, 16);
    g1.ctx.fillStyle = PALETTE.grass_mid;
    g1.ctx.fillRect(0, 0, 16, 16);
    g1.ctx.fillStyle = PALETTE.grass_light;
    g1.ctx.fillRect(2, 2, 2, 2);
    g1.ctx.fillRect(8, 7, 2, 2);
    g1.ctx.fillRect(12, 12, 2, 2);
    g1.ctx.fillStyle = PALETTE.grass_dark;
    g1.ctx.fillRect(4, 11, 2, 1);
    g1.ctx.fillRect(10, 3, 2, 1);
    this.cache.set('tile_grass_1', g1.canvas);

    // Grass 2 (with tiny yellow buttercup)
    const g2 = this.createCanvas(16, 16);
    g2.ctx.drawImage(g1.canvas, 0, 0);
    g2.ctx.fillStyle = PALETTE.sunflower_yellow;
    g2.ctx.fillRect(6, 6, 3, 3);
    g2.ctx.fillStyle = PALETTE.white;
    g2.ctx.fillRect(7, 7, 1, 1);
    this.cache.set('tile_grass_flower', g2.canvas);

    // Plowed soil (warm farm furrows)
    const soil = this.createCanvas(16, 16);
    soil.ctx.fillStyle = PALETTE.soil_mid;
    soil.ctx.fillRect(0, 0, 16, 16);
    soil.ctx.fillStyle = PALETTE.soil_light;
    soil.ctx.fillRect(0, 1, 16, 2);
    soil.ctx.fillRect(0, 5, 16, 2);
    soil.ctx.fillRect(0, 9, 16, 2);
    soil.ctx.fillRect(0, 13, 16, 2);
    soil.ctx.fillStyle = PALETTE.soil_dark;
    soil.ctx.fillRect(0, 3, 16, 1);
    soil.ctx.fillRect(0, 7, 16, 1);
    soil.ctx.fillRect(0, 11, 16, 1);
    soil.ctx.fillRect(0, 15, 16, 1);
    // Dark 1px grid border
    soil.ctx.fillStyle = PALETTE.dark_outline;
    soil.ctx.strokeRect(0.5, 0.5, 15, 15);
    this.cache.set('tile_soil', soil.canvas);

    // Wooden fence horizontal & vertical
    const fh = this.createCanvas(16, 16);
    fh.ctx.fillStyle = PALETTE.wood_mid;
    fh.ctx.fillRect(0, 5, 16, 3);
    fh.ctx.fillRect(0, 10, 16, 3);
    fh.ctx.fillStyle = PALETTE.wood_dark;
    fh.ctx.fillRect(3, 2, 3, 13);
    fh.ctx.fillRect(11, 2, 3, 13);
    fh.ctx.fillStyle = PALETTE.wood_light;
    fh.ctx.fillRect(4, 3, 1, 11);
    this.cache.set('tile_fence_h', fh.canvas);

    // Wooden board tile
    const wb = this.createCanvas(16, 16);
    wb.ctx.fillStyle = PALETTE.wood_mid;
    wb.ctx.fillRect(0, 0, 16, 16);
    wb.ctx.fillStyle = PALETTE.wood_light;
    wb.ctx.fillRect(1, 1, 14, 2);
    wb.ctx.fillRect(1, 9, 14, 2);
    wb.ctx.fillStyle = PALETTE.wood_dark;
    wb.ctx.fillRect(0, 7, 16, 1);
    wb.ctx.fillRect(0, 15, 16, 1);
    wb.ctx.fillRect(7, 0, 1, 7);
    wb.ctx.fillRect(12, 8, 1, 7);
    this.cache.set('tile_wood_board', wb.canvas);
  }

  // 2. Caro Farm: Sunflower (X) & Carrot (O) in 5 Growth Frames + Thinking Chicken
  generateCaroSprites() {
    // Soft shadow under pieces
    const drawShadow = (ctx) => {
      ctx.fillStyle = PALETTE.shadow_soft;
      ctx.beginPath();
      ctx.ellipse(8, 14, 6, 2, 0, 0, Math.PI * 2);
      ctx.fill();
    };

    // Sunflower 5 frames (Frame 0: seed sprout, 1: seedling, 2: bud, 3: bloom, 4: happy bobbing)
    for (let f = 0; f < 5; f++) {
      const { canvas, ctx } = this.createCanvas(16, 16);
      drawShadow(ctx);

      if (f === 0) {
        // Tiny green sprout
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 11, 2, 3);
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(6, 9, 2, 2);
        ctx.fillRect(8, 10, 2, 2);
      } else if (f === 1) {
        // Small plant with stem & 2 leaves
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 8, 2, 6);
        ctx.fillStyle = PALETTE.leaf_mid;
        ctx.fillRect(4, 9, 3, 2);
        ctx.fillRect(9, 8, 3, 2);
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(5, 9, 2, 1);
      } else if (f === 2) {
        // Budding sunflower
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 7, 2, 7);
        ctx.fillStyle = PALETTE.leaf_mid;
        ctx.fillRect(3, 10, 4, 2);
        ctx.fillRect(9, 9, 4, 2);
        // Yellow bud
        ctx.fillStyle = PALETTE.sunflower_gold;
        ctx.fillRect(6, 4, 4, 4);
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(7, 5, 2, 2);
      } else {
        // Full bloom (f === 3 & 4 slight bob)
        const yOff = f === 4 ? -1 : 0;
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 8 + yOff, 2, 6 - yOff);
        ctx.fillStyle = PALETTE.leaf_mid;
        ctx.fillRect(3, 11 + yOff, 4, 2);
        ctx.fillRect(9, 10 + yOff, 4, 2);
        // Yellow petals ring
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(4, 2 + yOff, 8, 8);
        ctx.fillStyle = PALETTE.sunflower_gold;
        ctx.fillRect(3, 4 + yOff, 10, 4);
        ctx.fillRect(5, 1 + yOff, 6, 10);
        // Brown center
        ctx.fillStyle = PALETTE.sunflower_center;
        ctx.fillRect(6, 4 + yOff, 4, 4);
        ctx.fillStyle = PALETTE.soil_light;
        ctx.fillRect(7, 5 + yOff, 2, 2);
      }
      this.cache.set(`caro_sunflower_${f}`, canvas);
    }

    // Carrot 5 frames (Frame 0: sprout, 1: small foliage, 2: peak orange tip, 3: full carrot, 4: happy bobbing)
    for (let f = 0; f < 5; f++) {
      const { canvas, ctx } = this.createCanvas(16, 16);
      drawShadow(ctx);

      if (f === 0) {
        ctx.fillStyle = PALETTE.leaf_mid;
        ctx.fillRect(7, 10, 2, 4);
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(6, 8, 2, 2);
        ctx.fillRect(9, 9, 1, 2);
      } else if (f === 1) {
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(5, 6, 2, 4);
        ctx.fillRect(7, 5, 2, 5);
        ctx.fillRect(9, 6, 2, 4);
        ctx.fillStyle = PALETTE.carrot_orange;
        ctx.fillRect(7, 11, 2, 3);
      } else if (f === 2) {
        ctx.fillStyle = PALETTE.leaf_mid;
        ctx.fillRect(4, 4, 3, 5);
        ctx.fillRect(7, 3, 2, 6);
        ctx.fillRect(9, 4, 3, 5);
        ctx.fillStyle = PALETTE.carrot_orange;
        ctx.fillRect(6, 9, 4, 4);
        ctx.fillRect(7, 13, 2, 2);
      } else {
        const yOff = f === 4 ? -1 : 0;
        // Lush leafy top
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(4, 1 + yOff, 3, 5);
        ctx.fillRect(7, 0 + yOff, 2, 6);
        ctx.fillRect(9, 1 + yOff, 3, 5);
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(6, 5 + yOff, 4, 2);
        // Plump orange carrot
        ctx.fillStyle = PALETTE.carrot_orange;
        ctx.fillRect(5, 7 + yOff, 6, 4);
        ctx.fillRect(6, 11 + yOff, 4, 3);
        ctx.fillRect(7, 14 + yOff, 2, 1);
        ctx.fillStyle = PALETTE.carrot_deep;
        ctx.fillRect(10, 7 + yOff, 1, 4);
        ctx.fillRect(9, 11 + yOff, 1, 2);
      }
      this.cache.set(`caro_carrot_${f}`, canvas);
    }

    // Thinking Chicken (Gà gãi đầu khi máy suy nghĩ - 4 frames)
    for (let f = 0; f < 4; f++) {
      const { canvas, ctx } = this.createCanvas(16, 16);
      drawShadow(ctx);

      // Body (plump white)
      ctx.fillStyle = PALETTE.feather_white;
      ctx.fillRect(4, 5, 8, 7);
      ctx.fillRect(3, 7, 10, 4);
      ctx.fillStyle = PALETTE.feather_shade;
      ctx.fillRect(4, 11, 8, 1);
      ctx.fillRect(10, 7, 2, 4); // wing

      // Red comb on head
      ctx.fillStyle = PALETTE.comb_red;
      ctx.fillRect(6, 2, 3, 2);

      // Beak & eyes
      ctx.fillStyle = PALETTE.beak_orange;
      ctx.fillRect(2, 6, 2, 2);
      ctx.fillStyle = PALETTE.black;
      ctx.fillRect(5, 6, 1, 1);

      // Yellow legs
      ctx.fillStyle = PALETTE.beak_orange;
      ctx.fillRect(5, 12, 1, 2);
      ctx.fillRect(8, 12, 1, 2);

      // Scratching wing animation
      if (f % 2 === 1) {
        ctx.fillStyle = PALETTE.feather_white;
        ctx.fillRect(8, 3, 3, 3); // Wing up scratching head
        // Cute "?" question mark bubble
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(13, 2, 2, 1);
        ctx.fillRect(14, 3, 1, 2);
        ctx.fillRect(14, 6, 1, 1);
      }
      this.cache.set(`caro_thinking_chicken_${f}`, canvas);
    }
  }

  // 3. Ludo / Cờ Cá Ngựa: 4 Chicken colors, 6 Dice faces, Barns, Feathers
  generateLudoSprites() {
    const colors = [
      { name: 'red', hex: PALETTE.chicken_red, dark: '#9c2424', light: '#f07676' },
      { name: 'blue', hex: PALETTE.chicken_blue, dark: '#284d94', light: '#739ded' },
      { name: 'green', hex: PALETTE.chicken_green, dark: '#2b783c', light: '#72d987' },
      { name: 'yellow', hex: PALETTE.chicken_yellow, dark: '#b07b15', light: '#fed96c' },
    ];

    colors.forEach((c) => {
      // Idle chicken 2 frames
      for (let f = 0; f < 2; f++) {
        const { canvas, ctx } = this.createCanvas(16, 16);
        ctx.fillStyle = PALETTE.shadow_soft;
        ctx.beginPath();
        ctx.ellipse(8, 14, 5, 2, 0, 0, Math.PI * 2);
        ctx.fill();

        const y = f === 1 ? -1 : 0;
        // Body
        ctx.fillStyle = c.hex;
        ctx.fillRect(4, 5 + y, 8, 7);
        ctx.fillRect(3, 7 + y, 10, 4);
        ctx.fillStyle = c.light;
        ctx.fillRect(5, 5 + y, 3, 2);
        ctx.fillStyle = c.dark;
        ctx.fillRect(4, 11 + y, 8, 1);
        ctx.fillRect(10, 7 + y, 2, 3); // wing

        // Comb & Beak
        ctx.fillStyle = PALETTE.comb_red;
        ctx.fillRect(6, 2 + y, 3, 2);
        ctx.fillStyle = PALETTE.beak_orange;
        ctx.fillRect(2, 6 + y, 2, 2);
        ctx.fillStyle = PALETTE.black;
        ctx.fillRect(5, 6 + y, 1, 1);

        // Legs
        ctx.fillStyle = PALETTE.beak_orange;
        ctx.fillRect(5, 12, 1, 2);
        ctx.fillRect(8, 12, 1, 2);

        this.cache.set(`ludo_chick_${c.name}_idle_${f}`, canvas);
        if (f === 0) {
          this.cache.set(`ludo_chick_${c.name}_idle`, canvas);
          this.cache.set(`ludo_chick_${c.name}`, canvas);
        }
      }

      // Hopping chicken 4 frames (jump arc)
      for (let f = 0; f < 4; f++) {
        const { canvas, ctx } = this.createCanvas(16, 16);
        const jumpH = [0, -3, -5, -2][f];
        const shadowScale = [5, 4, 3, 4][f];

        ctx.fillStyle = PALETTE.shadow_soft;
        ctx.beginPath();
        ctx.ellipse(8, 14, shadowScale, 2, 0, 0, Math.PI * 2);
        ctx.fill();

        ctx.fillStyle = c.hex;
        ctx.fillRect(4, 5 + jumpH, 8, 7);
        ctx.fillRect(3, 7 + jumpH, 10, 4);
        ctx.fillStyle = PALETTE.comb_red;
        ctx.fillRect(6, 2 + jumpH, 3, 2);
        ctx.fillStyle = PALETTE.beak_orange;
        ctx.fillRect(2, 6 + jumpH, 2, 2);
        ctx.fillStyle = PALETTE.black;
        ctx.fillRect(5, 6 + jumpH, 1, 1);

        // Fluttering wing
        ctx.fillStyle = c.light;
        ctx.fillRect(9, 4 + jumpH, 3, 4);

        this.cache.set(`ludo_chick_${c.name}_hop_${f}`, canvas);
        if (f === 0) {
          this.cache.set(`ludo_chick_${c.name}_hop`, canvas);
        }
      }

      // Feather burst particle for kick effect
      const fp = this.createCanvas(4, 4);
      fp.ctx.fillStyle = c.hex;
      fp.ctx.fillRect(1, 0, 2, 4);
      fp.ctx.fillRect(0, 1, 4, 2);
      this.cache.set(`ludo_feather_${c.name}`, fp.canvas);
    });

    // 6 Dice Faces (16x16 with 3D bevel & dots)
    const dotCoords = [
      [],
      [[7, 7]],
      [[4, 4], [10, 10]],
      [[4, 4], [7, 7], [10, 10]],
      [[4, 4], [10, 4], [4, 10], [10, 10]],
      [[4, 4], [10, 4], [7, 7], [4, 10], [10, 10]],
      [[4, 3], [10, 3], [4, 7], [10, 7], [4, 11], [10, 11]],
    ];

    for (let face = 1; face <= 6; face++) {
      const { canvas, ctx } = this.createCanvas(16, 16);
      // Wood/white block with 1px border
      ctx.fillStyle = PALETTE.dark_outline;
      ctx.fillRect(0, 0, 16, 16);
      ctx.fillStyle = PALETTE.parchment_light;
      ctx.fillRect(1, 1, 14, 13);
      ctx.fillStyle = PALETTE.parchment_mid;
      ctx.fillRect(1, 13, 14, 2);

      // Red dot for 1 (traditional Asian ludo), dark red/black for others
      ctx.fillStyle = face === 1 ? PALETTE.comb_red : PALETTE.dark_navy;
      const dots = dotCoords[face];
      dots.forEach(([dx, dy]) => {
        const dotSize = face === 1 ? 4 : 2;
        const x = face === 1 ? 6 : dx;
        const y = face === 1 ? 6 : dy;
        ctx.fillRect(x, y, dotSize, dotSize);
      });
      this.cache.set(`ludo_dice_${face}`, canvas);
    }
  }

  // 4. Snake / Đàn Gà Con: Mother hen (4 dirs), Baby chicks, 4 crops
  generateSnakeSprites() {
    const directions = ['right', 'left', 'down', 'up'];

    // Mother Hen in 4 directions x 4 walk frames
    directions.forEach((dir) => {
      for (let f = 0; f < 4; f++) {
        const { canvas, ctx } = this.createCanvas(16, 16);
        ctx.fillStyle = PALETTE.shadow_soft;
        ctx.beginPath();
        ctx.ellipse(8, 14, 6, 2, 0, 0, Math.PI * 2);
        ctx.fill();

        const bob = f % 2 === 1 ? -1 : 0;

        ctx.fillStyle = PALETTE.feather_white;
        ctx.fillRect(3, 4 + bob, 10, 8);
        ctx.fillStyle = PALETTE.feather_shade;
        ctx.fillRect(4, 11 + bob, 8, 1);
        ctx.fillStyle = PALETTE.comb_red;
        ctx.fillRect(6, 1 + bob, 4, 3);

        ctx.fillStyle = PALETTE.beak_orange;
        if (dir === 'right') {
          ctx.fillRect(13, 6 + bob, 2, 2);
          ctx.fillStyle = PALETTE.black;
          ctx.fillRect(10, 5 + bob, 1, 1);
        } else if (dir === 'left') {
          ctx.fillRect(1, 6 + bob, 2, 2);
          ctx.fillStyle = PALETTE.black;
          ctx.fillRect(5, 5 + bob, 1, 1);
        } else if (dir === 'up') {
          ctx.fillRect(7, 2 + bob, 2, 2);
        } else {
          ctx.fillRect(7, 8 + bob, 2, 2);
          ctx.fillStyle = PALETTE.black;
          ctx.fillRect(5, 6 + bob, 1, 1);
          ctx.fillRect(10, 6 + bob, 1, 1);
        }

        // Animated feet
        ctx.fillStyle = PALETTE.beak_orange;
        if (f % 2 === 0) {
          ctx.fillRect(5, 12, 2, 2);
          ctx.fillRect(9, 13, 2, 1);
        } else {
          ctx.fillRect(5, 13, 2, 1);
          ctx.fillRect(9, 12, 2, 2);
        }

        this.cache.set(`snake_hen_${dir}_${f}`, canvas);
        if (f === 0) {
          this.cache.set(`snake_hen_${dir}`, canvas);
        }
      }
    });

    // Baby chick body segment (cute yellow fluffy ball)
    for (let f = 0; f < 2; f++) {
      const { canvas, ctx } = this.createCanvas(16, 16);
      ctx.fillStyle = PALETTE.shadow_soft;
      ctx.beginPath();
      ctx.ellipse(8, 13, 4, 2, 0, 0, Math.PI * 2);
      ctx.fill();

      const bob = f === 1 ? -1 : 0;
      ctx.fillStyle = PALETTE.chicken_yellow;
      ctx.fillRect(4, 5 + bob, 8, 7);
      ctx.fillRect(3, 7 + bob, 10, 3);
      ctx.fillStyle = '#fce588';
      ctx.fillRect(5, 6 + bob, 3, 2);
      ctx.fillStyle = PALETTE.beak_orange;
      ctx.fillRect(2, 8 + bob, 2, 2); // tiny beak
      ctx.fillStyle = PALETTE.black;
      ctx.fillRect(4, 7 + bob, 1, 1); // cute eye
      ctx.fillStyle = PALETTE.beak_orange;
      ctx.fillRect(6, 12, 1, 2);
      ctx.fillRect(9, 12, 1, 2);

      this.cache.set(`snake_chick_${f}`, canvas);
      if (f === 0) {
        this.cache.set('snake_chick', canvas);
      }
    }

    // 4 Crops (Food): Carrot, Strawberry, Corn, Pumpkin
    const foods = [
      { name: 'carrot', draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(7, 2, 2, 4);
        ctx.fillRect(9, 3, 2, 3);
        ctx.fillStyle = PALETTE.carrot_orange;
        ctx.fillRect(5, 6, 6, 4);
        ctx.fillRect(6, 10, 4, 3);
        ctx.fillRect(7, 13, 2, 2);
      }},
      { name: 'strawberry', draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(6, 3, 4, 2);
        ctx.fillStyle = PALETTE.strawberry_red;
        ctx.fillRect(4, 5, 8, 5);
        ctx.fillRect(5, 10, 6, 3);
        ctx.fillRect(7, 13, 2, 1);
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(6, 7, 1, 1);
        ctx.fillRect(9, 7, 1, 1);
        ctx.fillRect(7, 10, 1, 1);
      }},
      { name: 'corn', draw: (ctx) => {
        ctx.fillStyle = PALETTE.corn_green;
        ctx.fillRect(3, 6, 3, 7);
        ctx.fillRect(10, 6, 3, 7);
        ctx.fillStyle = PALETTE.corn_yellow;
        ctx.fillRect(6, 3, 4, 9);
        ctx.fillStyle = PALETTE.sunflower_gold;
        ctx.fillRect(7, 4, 2, 7);
      }},
      { name: 'pumpkin', draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 2, 2, 3);
        ctx.fillStyle = PALETTE.pumpkin_orange;
        ctx.fillRect(3, 5, 10, 8);
        ctx.fillStyle = PALETTE.pumpkin_dark;
        ctx.fillRect(6, 5, 1, 8);
        ctx.fillRect(9, 5, 1, 8);
      }},
    ];

    foods.forEach((f, idx) => {
      const { canvas, ctx } = this.createCanvas(16, 16);
      ctx.fillStyle = PALETTE.shadow_soft;
      ctx.beginPath();
      ctx.ellipse(8, 14, 5, 2, 0, 0, Math.PI * 2);
      ctx.fill();
      f.draw(ctx);
      this.cache.set(`crop_${f.name}`, canvas);
      this.cache.set(`snake_crop_${idx}`, canvas);
      this.cache.set(`snake_crop_${f.name}`, canvas);
    });
  }

  // 5. 2048 / Gộp Nông Sản: 11 Crop Tiers (16x16 with badge values)
  generate2048Sprites() {
    const crops = [
      { tier: 1, val: 2, name: 'Hạt giống', color: PALETTE.soil_light, draw: (ctx) => {
        ctx.fillStyle = PALETTE.soil_dark;
        ctx.fillRect(5, 7, 6, 5);
        ctx.fillStyle = PALETTE.dirt_path;
        ctx.fillRect(6, 8, 4, 3);
      }},
      { tier: 2, val: 4, name: 'Mầm cây', color: PALETTE.leaf_light, draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 7, 2, 6);
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(4, 5, 3, 3);
        ctx.fillRect(9, 6, 3, 2);
      }},
      { tier: 3, val: 8, name: 'Bắp cải', color: PALETTE.cabbage_green, draw: (ctx) => {
        ctx.fillStyle = PALETTE.cabbage_green;
        ctx.fillRect(3, 4, 10, 9);
        ctx.fillStyle = PALETTE.cabbage_light;
        ctx.fillRect(5, 6, 6, 5);
      }},
      { tier: 4, val: 16, name: 'Cà rốt', color: PALETTE.carrot_orange, draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_light;
        ctx.fillRect(7, 2, 2, 3);
        ctx.fillStyle = PALETTE.carrot_orange;
        ctx.fillRect(5, 5, 6, 6);
        ctx.fillRect(6, 11, 4, 3);
      }},
      { tier: 5, val: 32, name: 'Khoai tây', color: PALETTE.potato_tan, draw: (ctx) => {
        ctx.fillStyle = PALETTE.potato_dark;
        ctx.fillRect(3, 4, 10, 8);
        ctx.fillStyle = PALETTE.potato_tan;
        ctx.fillRect(4, 5, 8, 6);
      }},
      { tier: 6, val: 64, name: 'Dâu tây', color: PALETTE.strawberry_red, draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_mid;
        ctx.fillRect(6, 3, 4, 2);
        ctx.fillStyle = PALETTE.strawberry_red;
        ctx.fillRect(4, 5, 8, 7);
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(6, 7, 1, 1);
        ctx.fillRect(9, 9, 1, 1);
      }},
      { tier: 7, val: 128, name: 'Cà chua', color: PALETTE.tomato_red, draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 3, 2, 2);
        ctx.fillStyle = PALETTE.tomato_red;
        ctx.fillRect(3, 5, 10, 8);
        ctx.fillStyle = '#ff7b7b';
        ctx.fillRect(5, 6, 2, 2);
      }},
      { tier: 8, val: 256, name: 'Bắp ngô', color: PALETTE.corn_yellow, draw: (ctx) => {
        ctx.fillStyle = PALETTE.corn_green;
        ctx.fillRect(3, 6, 3, 7);
        ctx.fillStyle = PALETTE.corn_yellow;
        ctx.fillRect(6, 3, 4, 10);
      }},
      { tier: 9, val: 512, name: 'Dưa hấu', color: PALETTE.watermelon_green, draw: (ctx) => {
        ctx.fillStyle = PALETTE.watermelon_green;
        ctx.fillRect(2, 4, 12, 9);
        ctx.fillStyle = PALETTE.watermelon_light;
        ctx.fillRect(4, 4, 2, 9);
        ctx.fillRect(8, 4, 2, 9);
        ctx.fillRect(12, 4, 1, 9);
      }},
      { tier: 10, val: 1024, name: 'Bí ngô', color: PALETTE.pumpkin_orange, draw: (ctx) => {
        ctx.fillStyle = PALETTE.leaf_dark;
        ctx.fillRect(7, 2, 2, 3);
        ctx.fillStyle = PALETTE.pumpkin_orange;
        ctx.fillRect(2, 5, 12, 9);
        ctx.fillStyle = PALETTE.pumpkin_dark;
        ctx.fillRect(5, 5, 1, 9);
        ctx.fillRect(10, 5, 1, 9);
      }},
      { tier: 11, val: 2048, name: 'Bí ngô vàng', color: PALETTE.pumpkin_gold, draw: (ctx) => {
        // Glowing golden pumpkin with magical aura
        ctx.fillStyle = PALETTE.pumpkin_glow;
        ctx.fillRect(1, 4, 14, 11);
        ctx.fillStyle = PALETTE.pumpkin_gold;
        ctx.fillRect(2, 5, 12, 9);
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(5, 5, 1, 9);
        ctx.fillRect(10, 5, 1, 9);
        ctx.fillStyle = PALETTE.white;
        ctx.fillRect(4, 7, 2, 2); // shine
      }},
    ];

    crops.forEach((c) => {
      const { canvas, ctx } = this.createCanvas(16, 16);
      ctx.fillStyle = PALETTE.shadow_soft;
      ctx.beginPath();
      ctx.ellipse(8, 14, 6, 2, 0, 0, Math.PI * 2);
      ctx.fill();
      c.draw(ctx);
      this.cache.set(`crop_2048_tier_${c.tier}`, canvas);
      this.cache.set(`crop2048_${c.tier}`, canvas);
    });
  }

  // 6. Pixel Emoji Sprites for Quick Chat (10 emojis)
  generateEmojiSprites() {
    const emojis = ['smile', 'thumb', 'heart', 'cry', 'angry', 'chicken', 'sunflower', 'carrot', 'trophy', 'gg'];
    emojis.forEach((em) => {
      const { canvas, ctx } = this.createCanvas(16, 16);
      ctx.fillStyle = PALETTE.parchment_light;
      ctx.fillRect(1, 1, 14, 14);
      ctx.fillStyle = PALETTE.dark_outline;
      ctx.strokeRect(0.5, 0.5, 15, 15);

      if (em === 'smile') {
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(3, 3, 10, 10);
        ctx.fillStyle = PALETTE.black;
        ctx.fillRect(5, 5, 2, 2);
        ctx.fillRect(9, 5, 2, 2);
        ctx.fillRect(5, 9, 6, 2);
      } else if (em === 'heart') {
        ctx.fillStyle = PALETTE.comb_red;
        ctx.fillRect(4, 4, 3, 3);
        ctx.fillRect(9, 4, 3, 3);
        ctx.fillRect(3, 6, 10, 3);
        ctx.fillRect(5, 9, 6, 2);
        ctx.fillRect(7, 11, 2, 2);
      } else if (em === 'trophy') {
        ctx.fillStyle = PALETTE.sunflower_yellow;
        ctx.fillRect(4, 3, 8, 6);
        ctx.fillRect(7, 9, 2, 3);
        ctx.fillRect(5, 12, 6, 2);
      } else {
        // Simple letter icon badge
        ctx.fillStyle = PALETTE.sunflower_gold;
        ctx.fillRect(3, 3, 10, 10);
      }
      this.cache.set(`emoji_${em}`, canvas);
    });
  }
}

export const spriteAtlas = new SpriteAtlas();
