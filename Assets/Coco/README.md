# Coco Courier: 10 Lanes — prototype 1

Unity 6000.3.20f1. Uses the existing URP, Input System and uGUI packages.
The game now uses generated pixel-art atlases, a bundled Pixelify Sans font, sprite renderers and standard Unity UI. All asset files and the font license are included locally. See `ArtSource/ART_GUIDE.md` for exact generation prompts, atlas layout and target dimensions.

## Launch
Open `Assets/Scenes/MainMenu.unity`, then press Play. MainMenu is build index 0; CocoCourier is index 1.
On first script import, the editor setup creates the scene and settings if missing.
`Coco Courier > Create or Open Main Menu` opens the menu; `Create or Open Game Scene` opens the road directly. No scene wiring is necessary.

## Controls and rules
- Tap **NEXT**, or press **Space**, to cross exactly one lane.
- Input during a jump is ignored. Hold does not repeat.
- Wait on any island to choose a gap. Traffic alternates direction and gets faster across the route.
- The dark ground marker shows the real position. The green courier follows a visual arc; it is vulnerable throughout the crossing.
- Land on all ten islands to deliver. A collision ends the attempt.
- **TRY AGAIN** resets the timer, progress and traffic after either result.
- The delivery timer starts immediately, includes waiting, and stops at the result. There is no timeout.

## Editable settings
Select `Assets/Coco/Resources/CourierSettings.asset` in the Inspector:
- Jump Duration: seconds to cross one lane.
- Arc Height: visual displacement only, with no effect on collision.
- Minimum/Maximum Car Speed: world units/second; deterministic per-lane speed across this range.
- Minimum/Maximum Spawn Interval: randomized seconds between cars on each lane.
- Perfect Delivery Seconds / One Point Delivery Seconds: endpoints of the customer rating curve.

Rating is the rounded linear interpolation from 10 at or below the perfect time to 1 at or above the slow time. Failed attempts do not change the average. Completed delivery count and rating sum are saved immediately with PlayerPrefs under `CocoCourier.*.v1`; the average survives app restarts. No personal data is stored.

## Delivery coins and future shop
A completed order earns **50 delivery coins + 5 coins per customer rating point**: 55 at 1/10, 75 at 5/10 and 100 at 10/10. Coins are awarded only after the tenth landing. Partial crossings, collisions and retries award zero; previous savings are retained.

`CourierEconomy.cs` separates reward calculation from movement. Each attempt has a unique order ID and an in-memory claimed flag; the save also keeps the last paid order ID to reject duplicate payment. The HUD shows the wallet, while the result shows the delivery reward, rating bonus, total earned and balance. Rating remains separate from the spendable currency.

The versioned JSON document in PlayerPrefs key `CocoCourier.Economy.v1` is saved immediately on payment. It contains balance, lifetime earned coins, the last paid order and fields for owned item IDs plus equipped courier/bag/trail IDs. The menu now uses these fields for the Sunrise bag reward and equipment. Other cosmetic purchases remain for a later stage. Existing ratings are retained and old deliveries do not grant retroactive coins. Reward constants are `OrderReward.DeliveryCoins` and `OrderReward.CoinsPerRatingPoint`.

## Main menu, food and daily dispatch
- PLAY opens a fresh delivery. MAIN MENU is available after a crash or delivery; zero-energy retry opens the food shop.
- New and migrated wallets receive 10/10 energy. Only the first accepted jump spends one energy. All subsequent jumps in that attempt are free; collisions do not refund it. Entering the game or pressing retry does not spend energy.
- One energy recovers every 600 seconds, including offline time, capped at ten. Food instantly restores energy: APPLE +2 / 20 coins, SANDWICH +5 / 40 coins, HOT MEAL +10 / 65 coins. The shop shows actual restoration after the cap and disables purchases when full or unaffordable.
- A stored sandwich restores up to five energy and can be eaten from menu or shop. Full energy never consumes stored food.
- DAILY DISPATCH grants 40, 50, 60, 70, 80, 100, 150 coins over seven claim days, plus one stored sandwich per claim. A missed day does not reset progress or create accumulated rewards. The first seventh claim unlocks SUNRISE BAG; later seventh claims grant 150 coins and a sandwich again. Select CLASSIC or SUNRISE in the shop to change the bag in menu and gameplay.
- Daily availability uses the UTC calendar, with a visible countdown to midnight UTC. The prototype uses local device UTC and the last saved timestamp to prevent simple backward clock changes; it has no server-time validation. Regeneration, daily receipts, coins, food stock and equipment are saved together under the existing economy key. Existing ratings and coins are retained.
- HOW TO PLAY opens instructions; GOT IT closes them. Escape closes menu overlays. No order or delivery timer runs in the menu; moving cars are decorative pixel-art sprites.
## Structure
- `CourierMenu.cs`: main menu, daily dispatch, food shop, bag selection and instructions.
- `CourierMenuVerification.cs`: menu transitions and energy/daily/store verification.
- `CourierSettings.cs`: ScriptableObject tuning.
- `CourierRun.cs`: ground-space simulation, continuous swept collision, rating and persistence.
- `CourierEconomy.cs`: rewards, persistent wallet and future inventory save schema.
- `CourierGame.cs`: scene construction, camera follow, portrait/safe-area HUD, input and results.
- `CourierProjectSetup.cs`: editor scene generation and automated verification.

The scene intentionally stores a camera and one controller; runtime objects are built in Awake. All ten lanes and eleven islands exist at once. The orthographic camera follows the ground position and keeps a 9.6-unit horizontal view in portrait for approaching cars.

## Verification
Run against a closed project or a disposable copy:

    Unity.exe -batchmode -projectPath <project> -executeMethod CocoCourier.Editor.CourierMenuVerification.RunBatch -logFile <log>

The verifier checks waiting, ignored repeated input, landing progress, ten-jump delivery, frozen results, rating endpoints, airborne collision, swept fast-car collision, ten traffic lanes and safe waiting. It then enters actual Play Mode, checks UI creation and both result/retry paths, verifies saved rating count and restores the pre-test career data. Reports go to `Logs/CocoVerification.txt`; a runtime screenshot goes to `Logs/CocoPortrait.png`.

Physical-device touch/notch testing and a signed Android/iOS build remain outside this prototype's desktop checks.

Verified on 2026-09-24 in Unity 6000.3.20f1 using an isolated copy because the main project was already open: C# compilation succeeded, all 15 simulation assertions and all 7 Play Mode assertions passed. Portrait gameplay, collision result and delivery result were rendered at 540 x 960 and inspected. No physical phone build was run. Full transcript: `Logs/CocoVerifyUnity.log`; summary: `Logs/CocoVerification.txt`.

Environment note: the last batch run also logged an internal `UnityEditor.Search.SearchDatabase` index exception during editor startup. Its stack does not enter Coco code, and the gameplay verification continued and passed. No Coco compilation or gameplay exception was observed. Screenshots: `Logs/CocoPortrait.png`, `Logs/CocoCrash.png`, `Logs/CocoDelivery.png`.


Economy update verified on 2026-09-24: compilation succeeded; 25 simulation/economy assertions and 16 Play Mode assertions passed. Checks cover 55/75/100 coin rewards, no partial rewards, repeated and older claims, save reload, lifetime earnings, unchanged savings after a later crash, result breakdown and retry. Portrait screenshots inspected at 540 x 960. The same internal Unity SearchDatabase indexing exception was observed, without Coco runtime errors. Transcript: `Logs/CocoEconomyUnity.log`; summary: `Logs/CocoEconomyVerification.txt`.


Menu update verified in Unity 6000.3.20f1: 35 service assertions and 17 actual Play Mode checks passed (52 total). This covers old-save migration, food costs and caps, offline recovery, daily rewards across skipped days and clock rollback, bag unlock, menu/shop/help UI, first-jump energy charging, crash/delivery/retry/menu transitions and hungry routing. Final portrait UI was rendered at 540 x 960 and inspected. MainMenu is first in Build Settings. Physical device/notch testing is still pending. Unity's existing internal SearchDatabase startup exception remains separate from the passing game checks. Report: `Logs/CocoMenuVerification.txt`; transcript: `Logs/CocoMenuUnity.log`; previews: `Logs/MainMenu.png` and `Logs/FoodShop.png`.


## Pixel art conversion
Generated atlases now cover the courier (Classic and Sunrise bags), eight chicken customers, four vehicles, food, coins, bags, parcel, envelope, road/island/sidewalk tiles, tree, UI frames/buttons and logo. `PixelArt.cs` maps measured atlas regions and fits sprites to existing UI/world bounds. `PixelArtImporter.cs` guarantees Point filtering, readable alpha, uncompressed textures and no mipmaps. `PixelArtVerification.cs` validates all atlases and then runs the menu/game regression checks.

The courier is now about 73 pixels tall at 540 x 960. The portrait camera shows 9.6 world units across; collision stays on the existing ground-space simulation, independent of the enlarged visual sprite and jump pose. Customers cycle after successful deliveries, with matching sprites on the final island, in the HUD and in the result. The title uses generated pixel lettering; dynamic English text uses the bundled Pixelify Sans font (OFL license included).

Exact generation prompts and display dimensions are in `ArtSource/ART_GUIDE.md` and `ArtSource/SUNRISE_PROMPT.md`. Assets are included in `Resources/PixelArt`; no file outside the project is required. Use `CocoCourier.Editor.PixelArtVerification.RunBatch` for the pixel-art validation suite.

Pixel conversion verification: compilation passed; all five atlases, eight customers, both four-pose courier variants, sixteen world sprites, eight UI sprites and the font loaded successfully. All 35 service checks and 19 actual Play Mode checks passed (54 total). Portrait menu, gameplay, airborne courier, store, instructions, hungry state, delivery result and customer gallery inspected at 540 x 960. The pre-existing UnityEditor.Search.SearchDatabase startup exception appeared again; no Coco gameplay exception was observed. Reports: `Logs/PixelArtVerification.txt`, `Logs/PixelArtGameplayChecks.txt`; transcript: `Logs/PixelArtUnity.log`. Device testing remains pending.


## Contracts, traffic and career update
All player-facing text remains English. Launch MainMenu, choose PLAY, read the contract and press ACCEPT ORDER, then tap NEXT or press Space. The briefing pauses the simulation and delivery timer. Energy is charged only on the first jump.

- Exactly ten lanes remain. Lane base speed interpolates between minimumCarSpeed and maximumCarSpeed, with an additional 1 to finalLaneSpeedMultiplier ramp. Matching vehicle types accelerate toward the destination. Later shift routes add shiftSpeedStep per stage. Cars, long slower vans and compact fast scooters have their own visible shapes and matching collision dimensions. The HUD announces the next vehicle class and direction.
- Contracts follow the eight clients: office clerk/chef/executive request HOT LUNCH within expressDeadline; builder/mechanic request HEAVY PARCEL with longer jumps; doctor/teacher/gardener request HANDLE WITH CARE with no close calls. Missing an optional condition still allows delivery and its normal rating reward.
- A CLOSE CALL requires a narrow pass behind departing traffic, no collision, and a safe landing. At most one tip per lane; waiting cannot farm tips. Tips are displayed immediately but paid only on delivery. A crash pays zero.
- Delivery pays 50 + rating * 5 + close calls * closeCallCoins + completed contract bonus. Contract bonus defaults to 30, doubled on the third/special order. Every third consecutive successful delivery pays streakCoins (75). A crash or abandonment of a paid route resets the streak, preserving coins and reputation.
- A shift contains three paid attempts: regular, busy and special. Failed attempts still count. After the third result, its summary displays deliveries and total earned coins; three successes also pay shiftCoins (40). The next shift starts automatically. Shift progress and streak survive normal restarts; an unfinished paid route is resolved as abandoned once on the next scene/session entry.
- Each successful delivery adds one trust point to that client. Three points unlock their cosmetic hat. CLIENTS opens the eight-client reputation/equipment screen; WEAR equips, NO HAT removes. Cosmetics do not affect movement, collision, energy or rewards. Hats and the scooter are code-native pixel shapes; existing generated atlases are reused.
- All additional timing, speed and bonus parameters are exposed in Resources/CourierSettings.asset. Food, recovery and daily dispatch keep their existing behavior. No advertisements, betting or gambling features.

New source files: Scripts/DeliveryContract.cs, Scripts/CourierCosmetics.cs and Editor/CourierFeatureVerification.cs (plus Unity metadata). Updated: CourierRun.cs, CourierEconomy.cs, CourierGame.cs, CourierMenu.cs, CourierSettings.cs, CourierSettings.asset and CourierMenuVerification.cs.

Feature validation uses PixelArtVerification.RunBatch, which runs the feature service suite and actual Play Mode transitions. Reports: Logs/CourierFeatureChecks.txt, Logs/CocoMenuVerification.txt, Logs/FeatureUnity.log. Tests restore saved player preferences. Physical phone testing is still pending.

Feature update verified on 2026-09-24 in Unity 6000.3.20f1: C# compilation passed, 40 feature/safe-window checks + 35 economy/energy/daily checks + 25 actual Play Mode checks passed (100 total). Twelve seeded hardest-stage routes remained completable by waiting for safe windows. Briefing, reputation screen, close-call feedback, shift summary and streak rewards were rendered at 540 x 960 and inspected. Source scripts match the tested copy. The existing UnityEditor.Search.SearchDatabase startup exception remains; no Coco compile/runtime errors were observed. Physical-device testing was not performed.


## Denser traffic and scooter revision
Spawn intervals reduced from 2.4-4.2 seconds to 1.2-2.0 seconds per lane (approximately twice as frequent). Each lane starts filled with correctly spaced moving vehicles; its first scheduled spawn continues that spacing. Speed progression remains unchanged. The primitive scooter was replaced by a generated transparent pixel-art moped with a helmeted chicken rider; it mirrors with travel direction and keeps its ground collision size. Asset and exact built-in image-generation prompt: ArtSource/SCOOTER_PROMPT.md.

Verified in Unity 6000.3.20f1: compilation passed; 42 feature checks, 35 service checks and 25 Play Mode checks passed (102 total). All 12 hardest-route safe-window scenarios passed with denser traffic. ScooterTraffic.png inspected at 540x960. Existing unrelated Unity SearchDatabase startup exception remains. Reports: Logs/TrafficUnity.log, Logs/TrafficFeatureChecks.txt, Logs/TrafficPlayChecks.txt. No physical phone test.


## Kitchen, last meter, lost parcels and Lunch Rush
- KITCHEN opens a free 35-second cooking round, also available through COOK A MEAL on the hungry result and food shop. Tap conveyor ingredients in the recipe order (bread, two fillings, bread). Wrong input resets only the current sandwich. Completing 1-3 recipes earns one stored sandwich; 4 or more earns two, capped per round. Leaving early pays nothing. Finished-round rewards and personal best are saved once. Stored sandwiches retain the usual +5 energy effect and work from zero energy.
- THE LAST METER appears after a successful delivery. Stop the moving marker in the green zone with THROW or Space to earn 15 additional coins. The parcel animates toward the client. A miss or SKIP leaves the already-paid delivery untouched. Tips are included in the displayed total and ordinary shift earnings, never in delivery time or the rating. No second payment from repeated taps.
- LOST PARCEL appears on one island from 3 through 6 on ordinary routes. PICK UP is optional; continuing with NEXT skips it. Accepting lengthens the remaining jumps by 15 percent, including on already-heavy orders. Finishing pays 40 extra coins; a crash pays nothing. The carried parcel remains visible alongside Coco.
- LUNCH RUSH is a separate ten-lane challenge using a fixed seed for comparable retries. It has 12 percent faster traffic, spawn intervals multiplied by 0.85, and a 45-second deadline. First jump costs one energy. Completion pays the normal base/rating/tip coins, but does not alter regular shifts, delivery streaks, client reputation or career rating. Bronze is earned by completion, silver at 30 seconds, gold at 20 seconds. Best time and highest badge persist; silver unlocks an equippable RUSH VISOR. Lost parcels are disabled here to keep attempts comparable. Timeout and collision both support retry.
- Additional tuning is exposed in the Activities section of CourierSettings.asset. Existing pixel atlases and Unity UI are reused; conveyor ingredient icons are code-native pixel shapes. All player-facing text is English.

Created files: Scripts/ActivityRounds.cs, Scripts/CourierMenuActivities.cs, Scripts/CourierGameActivities.cs, Editor/ActivityVerification.cs, plus Unity .meta files. Updated the existing game/menu/economy/simulation/settings/cosmetics, settings asset and regression runner. MainMenu and CocoCourier remain the launch scenes; all new panels are built automatically with no manual setup.

Verification on 2026-09-24, Unity 6000.3.20f1: compilation passed, 143 assertions passed (42 traffic/career, 35 economy/food/daily, 28 activity services and 38 actual Play Mode checks). Includes zero-energy kitchen entry and recovery, recipe errors/rewards/idempotency, parcel pickup and crash, precise/missed/skipped throws, repeated input, challenge timeout/retry/record/equipment and safe paths with stacked heavy parcels. The real dense Rush route was completed by a conservative simulated player in 8.82 seconds. UI renders at 540x960 were inspected for menu, kitchen, hungry cooking, parcel pickup, final throw, Rush rules and results. Source scripts match the tested isolated copy. Reports: Logs/ActivityChecks.txt, Logs/ActivitiesPlayChecks.txt, Logs/ActivitiesFeatureChecks.txt, Logs/ActivitiesUnity.log. The known unrelated Unity SearchDatabase startup exception still appears; no Coco compilation/runtime errors were found. Physical-phone testing was not performed.


## iOS texture size budget (2026-10-01)
Imported build textures now use these maximum resolutions on iOS, Android and Standalone: courier/courier_sunrise 256, clients/world/ui 512, scooter 128. High-resolution source PNGs are retained for future art edits; Unity imports and packages reduced versions. PixelArtImporter is the single configuration source, also used by the verification runner so checks cannot restore oversized settings.

RGBA32 readability is intentionally retained because PixelArt.Cell performs runtime alpha trimming with GetPixels32. Blind ASTC compression or disabling Read/Write would break this path. Mipmaps remain off and Point filtering remains enabled.

TextureSizeAudit.Run compiled successfully and verified all 41 runtime sprite slices. Raw RGBA pixels decreased from 37,741,776 bytes to 3,713,536 bytes (about 90.2 percent less); this is texture payload, not a measured IPA or TestFlight size. Reduced atlases are available in Logs/Reduced-*.png; report: Logs/TextureSizeAudit.txt.

CiBuild.BuildIOS uses CompressWithLz4HC and IL2CPP OptimizeSize. The iOS PlayerSettings value is persisted too. Fastlane now measures the archived .app payload (excluding symlink duplicates) before uploading and stops at >=90,000,000 bytes or if the archive cannot be located. This is a conservative local gate, not a substitute for Apple's final TestFlight download/install measurements. Actual distribution size requires a fresh signed Mac/Xcode build and Apple processing; Windows can validate the Unity export only.

Local iOS export verification completed successfully on 2026-10-01 via CiBuild.BuildIOS. Build report confirms 9.4 MiB of total user assets, including 8.0 MiB of textures (built-in/URP textures included). Coco atlas entries match 512/256/128 imports. The reported full export size includes generated C++ and static libraries and is not an installed app/IPA size. Log: Logs/SizeIOSBuild.log. Signed Xcode archive and the Fastlane size gate still require the Mac CI run.
