# Epochs Unbound: Design Document

Status: draft v0.1 (2026-10-08)

## 1. Vision

A real-time civilisation strategy game in the spirit of *Rise of Nations*. The player leads one society from its first settlement into the space age. There are no fixed caps: the world, the number of cities and the population grow as far as the player's people, resources and infrastructure can sustain them.

Three ideas carry the design:

1. **People are the economy and the army.** Every soldier, worker, scientist and engineer is a citizen drawn from the same population. Mobilising for war is an economic decision.
2. **Infrastructure is the cap.** Growth is limited by food, housing, logistics, power and administration, never by a hard number.
3. **The world keeps opening up.** The map expands as the player explores and as technology extends reach (ships, rail, aircraft, satellites).

## 2. Pillars and non-goals

**Pillars**
- Readable at a glance: the player can see why a city is starving or a front is stalling.
- Deep but systemic: a few interacting rules, not hundreds of special cases.
- Scales smoothly from one village to a continent-spanning empire.

**Non-goals (for now)**
- Hero units or RPG progression.
- Tactical per-soldier micro beyond formations and stances.
- Multiplayer. The simulation is written deterministically so it can be added later.

## 3. Eras

Technology is researched continuously. Eras are milestones that unlock new buildings, units and systems.

| # | Era | New systems unlocked |
|---|-----|----------------------|
| 1 | Settlement | Foraging, farming, wood and stone, housing, first borders |
| 2 | Classical | Cities, roads, markets, writing (research), professional soldiers |
| 3 | Medieval | Castles, guilds, sea trade, taxation, larger armies |
| 4 | Gunpowder | Firearms, artillery, universities, colonisation by sea |
| 5 | Enlightenment | Banking, early industry, standing armies, diplomacy treaties |
| 6 | Industrial | Railways, factories, coal and steel, mass conscription |
| 7 | Electric | Power grids, telegraph, electrified industry, early aircraft |
| 8 | Modern | Oil, motor transport, air forces, radio, mechanised war |
| 9 | Atomic | Nuclear power, missiles, jets, mass production |
| 10 | Information | Computers, satellites, guided missiles, global trade networks |
| 11 | Space | Launch sites, orbital infrastructure, space exploration victory |

## 4. Core loop

1. Explore and claim land.
2. Settle and grow cities; citizens are born, age, migrate and die.
3. Assign the workforce to food, materials, industry, research and the military.
4. Build infrastructure (roads, rail, grids) that lifts the limits on growth.
5. Trade, negotiate or fight with rival nations.
6. Research to reach the next era, which opens new resources and new limits.

## 5. Systems

### 5.1 World

- Procedurally generated terrain in fixed-size **chunks** (e.g. 256 × 256 m). Chunks are generated on demand as borders, scouts or vision reach them, so the map is effectively unbounded.
- Terrain layers: height, biome, water, fertility, and resource deposits (some hidden until a technology reveals them, e.g. oil, uranium).
- Chunks far from any activity are simulated at a coarse level (aggregate numbers, no individual agents) to keep cost flat as the world grows.

### 5.2 Camera

- Free 3D RTS camera: pan, rotate, tilt and zoom from street level up to a strategic map view.
- At high zoom-out the view switches to a strategic overlay (borders, fronts, trade flows) rather than rendering every entity.

### 5.3 Population

- Each citizen is an entity with age, location, home, job, skills and health.
- Births depend on food, housing and stability; deaths on age, health, famine and war.
- **Migration**: citizens move towards cities with jobs, housing and stability, including between the player's cities and from rival nations.
- Generations: skills and education improve across generations when schools and universities exist.
- At large scales, distant citizens are aggregated into **cohorts** (groups sharing city, age band and job) so the simulation stays affordable. Cohorts expand back into individuals when the player looks closely.

### 5.4 Economy and resources

- Primary: food, wood, stone, metal ore, and later coal, oil, uranium, rare materials.
- Processed goods: tools, weapons, steel, fuel, electricity, electronics.
- Every good is physically produced by workers in buildings and moved by logistics; there is no global stockpile beyond a city's storage.
- Money comes from taxes and trade, and pays wages, upkeep and purchases.

### 5.5 Cities and buildings

- No city cap. Founding a city needs settlers (citizens) and claimed land.
- City growth is limited by housing, food supply, water, sanitation and later power.
- Administration: cities far from the capital need governance buildings or suffer lower stability and efficiency. This replaces a hard city cap with a soft cost.

### 5.6 Logistics and transport

- Goods and people move along networks: paths → roads → rail → motorways, plus sea lanes and air routes.
- Throughput and travel time on each link matter; congestion is visible.
- Supply lines feed armies (see 5.8).

### 5.7 Research

- Research points come from scholars, universities and labs (all staffed by citizens).
- A technology web, not a single line; eras unlock when enough of the era's technologies are known.

### 5.8 Military

- **Soldiers are citizens.** Recruiting removes workers from the economy; casualties reduce the population.
- A unit needs: recruits, training time, equipment (produced goods) and ongoing supply (food, ammunition, fuel).
- Unsupplied units lose morale and strength.
- Demobilising returns surviving soldiers to the workforce.
- Unit families evolve across eras: infantry, cavalry, artillery, naval, armour, air, missiles.

### 5.9 Diplomacy

- Rival AI nations with their own economies and goals.
- Relations: war, peace, trade agreements, alliances, embargoes, borders open or closed.
- Trade routes between nations generate income for both sides and create dependencies.

### 5.10 Power grids

- From the Electric era: power plants produce electricity that travels along a grid to consumers.
- Industry and modern buildings run at reduced output without power. Grids can be attacked.

### 5.11 Missiles and space

- Missiles: long-range strikes that need production, launch sites and targeting.
- Satellites: reveal the map, improve communications and enable guided weapons.
- Space programme: a chain of launch, orbital and exploration projects; one of the victory paths.

## 6. Game modes

- **Sandbox / continuous empire**: start from one settlement, no time limit, play as long as you want.
- **Scenarios**: authored or generated objectives (survive a winter, win a war, reach orbit first) on a set map.

## 7. Technical approach

- **Engine**: Unity 6 LTS, C#, URP.
- **Simulation**: Unity Entities (DOTS/ECS) with Burst and the Job System for anything that scales with entity count (citizens, units, goods in transit). MonoBehaviour for UI, camera and tooling.
- **Fixed-step simulation** separate from rendering, written to be deterministic so saves are reliable and multiplayer stays possible.
- **Level of detail for simulation**: full agents near the player's attention, aggregated cohorts elsewhere (5.1, 5.3).
- **Data-driven content**: eras, technologies, buildings and units defined in data files (ScriptableObjects or JSON) so balance changes don't need code changes.
- **Save system** from the first milestone, since the game is about long-running empires.

## 8. Milestones

1. **M0 Foundations**: Unity project, chunked terrain that generates as you explore, RTS camera, fixed-step simulation clock.
2. **M1 First settlement**: one town centre, citizens who are born, age and take jobs, a food and wood loop, housing limits.
3. **M2 Expansion**: multiple cities, roads, migration between cities, stone and metal, basic research.
4. **M3 Conflict**: recruitment from the workforce, equipment and supply, one rival AI nation, basic combat.
5. **M4 Ages**: era progression through Medieval, trade and diplomacy.
6. Later milestones add rail, power grids, aviation, missiles and space, each as its own slice.

## 9. Open questions

- How much direct control does the player have over individual citizens versus job quotas per building?
- Victory conditions in sandbox mode: space only, or also conquest, economic and cultural paths?
- Population scale target for M1–M3 (e.g. 10k citizens at 60 fps on mid-range hardware) to set performance budgets.
