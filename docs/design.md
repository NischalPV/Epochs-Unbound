# Epochs Unbound: Design Document

Status: draft v0.2 (2026-10-08)

## 1. Vision

A real-time civilisation strategy game in the spirit of *Rise of Nations*. The player leads one society from its first settlement through the space age and into the age of superintelligence. There are no fixed caps: the world, the number of cities and the population grow as far as the player's people, resources and infrastructure can sustain them.

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
| 11 | Space | Launch sites, orbital infrastructure, space exploration |
| 12 | Artificial Intelligence | AI-assisted research and logistics, automated factories, autonomous units and drones, data centres as a new power-hungry building |
| 13 | Superintelligence | Self-improving research, near-full automation of labour, megaprojects, Superintelligence project as a victory path, new alignment and control risks |

The last two eras change the role of people. Automation can replace workers in most jobs, so population pressure shifts from labour to compute, power and stability. Unaligned or poorly controlled AI is a risk the player must manage (see 5.14).

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
Research happens at two levels.

**National research** (libraries, universities, labs):
- A technology web, not a single line; eras unlock when enough of the era's technologies are known.
- Covers broad advances: new eras, new building types, new unit families, civic and military doctrine.

**Building research** (in each building, as in *Rise of Nations*):
- Every production, military and civic building has its own short upgrade line, researched in that building and paid for with resources and time.
- Examples: a lumber camp researches better axes and then sawmills (more wood per worker); a farm researches crop rotation and irrigation; a barracks researches drill and better armour for its infantry; a market researches caravans and banking; a temple researches its faith tiers (see 5.13).
- A building upgrade applies to every building of that type in the nation once researched, so the player researches it once, not per city.
- Each upgrade line is gated by era and sometimes by a national technology, so the two levels feed each other.
- Upgrade lines are defined in data alongside the building (see section 7).

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

### 5.12 Markets and commerce

- The **market** is a city's commercial hub, available from the Classical era.
- **Exchange**: buy and sell resources for money. Prices follow supply and demand across the nation, so dumping one resource drives its price down.
- **Caravans and merchants**: a market sends caravans (later merchant ships, trains, cargo planes) along trade routes to other cities and to foreign nations. Income grows with distance and the size of both cities.
- **Commerce limit**: how much money a nation can earn per minute is capped by its markets and their research, so commerce scales with infrastructure like everything else.
- **Market research** (see 5.7): caravan speed, banking, stock exchange, global finance.
- Markets are where the economic victory is won (see section 6).

### 5.13 Temples and faith

- The **temple** is a city's religious and cultural centre, available from the Settlement era as a shrine and growing into temples, cathedrals and later cultural institutions.
- **Stability**: temples raise stability in their city, which reduces unrest, desertion and migration away.
- **Borders and influence**: faith extends national borders and can draw neighbouring towns and migrants towards the player's culture.
- **Defence**: enemy units in the player's territory suffer attrition, increased by temple research.
- **Faith research** (see 5.7): each temple tier unlocks stronger stability, larger border push and higher attrition.
- In later eras faith gradually blends into culture and ideology (media, education), but the building keeps the same role.

### 5.14 Artificial intelligence and superintelligence

- **AI era**: data centres turn power into compute. Compute speeds up research, runs automated factories and logistics, and powers drones and autonomous units. Automated jobs no longer need citizens, which frees people but can cause unemployment and instability if housing, jobs and welfare don't keep up.
- **Superintelligence era**: compute starts improving research by itself. The player chooses how much autonomy to grant: more autonomy gives faster progress but raises the risk of a misaligned AI incident (sabotaged infrastructure, rogue units, collapse of trust).
- **Alignment** is a resource line of its own: safety research, oversight institutions and treaties with other nations keep risk down.
- Completing the **Superintelligence project** with risk under control is a victory path.

## 6. Game modes

- **Sandbox / continuous empire**: start from one settlement, no time limit, play as long as you want.
- **Victory paths in sandbox**: conquest (take every rival capital), economic (dominate world trade through markets), space (complete the space programme) and superintelligence (complete the Superintelligence project safely). Players can turn individual paths off.
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
3. **M2 Expansion**: multiple cities, roads, migration between cities, stone and metal, national research and the first building upgrades, temples.
4. **M3 Conflict**: recruitment from the workforce, equipment and supply, one rival AI nation, basic combat.
5. **M4 Ages**: era progression through Medieval, markets and trade, diplomacy.
6. Later milestones add rail, power grids, aviation, missiles, space, AI and superintelligence, each as its own slice.

## 9. Decisions

- **Citizen control**: hybrid. Buildings have job quotas that free citizens fill automatically; the player can also select citizens and give them direct orders, which matters most in the early eras and in emergencies.
- **Victory**: several paths (conquest, economic, space, superintelligence); see section 6.
- **Performance target for M1–M3**: 10,000 citizens at 60 fps on a mid-range PC. Distant citizens are grouped into cohorts, so later eras can go beyond this.
