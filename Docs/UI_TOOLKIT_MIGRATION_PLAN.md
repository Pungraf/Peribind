# UI Toolkit Migration Plan (Peribind)

## Scope
- Preserve behavior parity first.
- Replace presentation/input layer scene-by-scene.
- Keep networking/auth/game logic classes unchanged unless required for UI wiring.

## Standards Applied
- Runtime UI Toolkit with `UIDocument` as entry point.
- Query elements from `rootVisualElement` by stable names.
- Register callbacks in `OnEnable`, unregister in `OnDisable`.
- Prefer class-based USS styling and shared variables; avoid heavy inline style mutation.
- Keep current business logic methods (scene changes, logout, auth/lobby calls) in existing controllers.

## Phase Order
1. Foundation assets and migration checklist.
2. Starter scene migration (low risk, stateless actions).
3. Login scene migration.
4. Lobby scene migration.
5. Profile scene migration.
6. Game HUD/palette migration.

## Current Progress
- Completed: Phase 0 foundation.
- Completed: Phase 1 implementation assets/scripts for Starter UI Toolkit.
- Completed: Phase 2 implementation assets/scripts for Login UI Toolkit (using existing `LoginMenu` logic).
- Completed: Phase 3 implementation assets/scripts for Lobby UI Toolkit (using existing `LobbyUgsMenu` logic).
- Completed: Phase 4 implementation assets/scripts for Profile UI Toolkit (using existing `PlayerProfileMenu` logic).
- Completed: Phase 5 implementation assets/scripts for Game HUD + Piece Palette UI Toolkit (using existing `GameHudView` and `PiecePaletteView` logic).
- Completed: Post-migration cleanup pass:
  - Removed uGUI fallback references from migrated controllers (`LoginMenu`, `LobbyUgsMenu`, `PlayerProfileMenu`, `GameHudView`, `PiecePaletteView`).
  - Removed username/password auth paths in `LoginMenu` and `UgsBootstrap` in favor of Unity Player Accounts browser flow only.
- Pending: Final Unity Editor parity validation after field cleanup.

## Added Files (Phase 0/1)
- `Assets/Scripts/Unity/UI/Toolkit/StarterMenuToolkitController.cs`
- `Assets/Resources/UI/Toolkit/Starter/StarterMenu.uxml`
- `Assets/Resources/UI/Toolkit/Common/PeribindTheme.uss`
- `Assets/Resources/UI/Toolkit/Starter/StarterMenu.uss`
- `Assets/Resources/UI/Toolkit/Login/LoginMenu.uxml`
- `Assets/Resources/UI/Toolkit/Login/LoginMenu.uss`
- `Assets/Resources/UI/Toolkit/Lobby/LobbyMenu.uxml`
- `Assets/Resources/UI/Toolkit/Lobby/LobbyMenu.uss`
- `Assets/Resources/UI/Toolkit/Profile/ProfileMenu.uxml`
- `Assets/Resources/UI/Toolkit/Profile/ProfileMenu.uss`
- `Assets/Resources/UI/Toolkit/Game/GameHud.uxml`
- `Assets/Resources/UI/Toolkit/Game/GameHud.uss`

## Starter Scene Wiring Steps
1. Open `StarterScene`.
2. Create GameObject `UIToolkitRoot`.
3. Add component `UIDocument`.
4. Add component `StarterMenuToolkitController`.
5. On `StarterMenuToolkitController`, assign `starterMenu` to existing `StarterUI` object (component `StarterMenu`).
6. Keep `autoAssignVisualTreeFromResources` and `autoAssignStylesFromResources` enabled.
7. Assign a runtime `PanelSettings` asset to `UIDocument` (create one if needed).
8. Disable old uGUI canvas object (`Canvas`) after Toolkit parity is confirmed.
9. Keep `EventSystem` with `InputSystemUIInputModule` active.

## Starter Parity Checklist
- `Play` triggers same behavior as `StarterMenu.LoadLobbyScene()`.
- `Profile` triggers same behavior as `StarterMenu.LoadProfileScene()`.
- `Logout` triggers same behavior as `StarterMenu.Logout()` including auth sign-out path.
- No duplicate actions per click (verify callback registration once).
- Scene transition targets remain unchanged from `StarterMenu` serialized fields.
- Input works with mouse and keyboard submit/focus.

## Validation Gate Before Phase 2
- Starter scene passes full parity checklist.
- Old Canvas can stay disabled for one full manual test cycle without regressions.
- No console errors from missing UI Toolkit elements or missing resources.

## Login Scene Wiring Steps
1. Open `LoginScene`.
2. Ensure a `UIDocument` exists (recommended object name: `UIToolkitRoot`).
3. Assign the same runtime `PanelSettings` used by Starter.
4. Keep `LoginMenu` component on `LoginController`; do not replace it.
5. In `LoginMenu` inspector:
   - Enable `enableUiToolkit`.
   - Leave `autoAssignUiDocument`, `autoAssignVisualTreeFromResources`, and `autoAssignStylesFromResources` enabled.
6. Keep existing flow dependencies wired:
   - `ugsBootstrap` -> `UGSManager` object.
   - `matchRegistryClient` -> `UGSManager` object.
7. First parity run with old `Canvas` enabled, then disable old login/register canvas hierarchy after successful parity.
8. Keep `EventSystem` with `InputSystemUIInputModule`.

## Login Parity Checklist
- Login panel opens by default.
- Register panel navigation works (`Create Account`, `Back To Login`).
- Browser login (`Sign In`) starts same Player Accounts flow.
- Browser register (`Create Account`) starts same Player Accounts flow.
- Cancel/restart behavior while flow is in progress still works.
- Version gate message behavior unchanged.
- Profile upsert + scene transition after auth unchanged.
- `Quit` still exits application.
- No console warnings for missing Toolkit element IDs.

## Lobby Scene Wiring Steps
1. Open `LobbyScene`.
2. Ensure a `UIDocument` exists (recommended object name: `UIToolkitRoot`).
3. Assign the same runtime `PanelSettings` used by Starter/Login.
4. Keep `LobbyUgsMenu` component on `LobbyController`; do not replace network logic.
5. In `LobbyUgsMenu` inspector:
   - Enable `enableUiToolkit`.
   - Leave `autoAssignUiDocument`, `autoAssignVisualTreeFromResources`, and `autoAssignStylesFromResources` enabled.
6. Keep existing flow dependencies wired:
   - `directConnection`
   - `matchRegistry`
   - `lobbyService` (or let auto-find resolve from scene)
7. First parity run with old Lobby canvas enabled, then disable old canvas after successful parity.
8. Keep `EventSystem` with `InputSystemUIInputModule`.

## Lobby Parity Checklist
- Create lobby uses same lobby name + map values.
- Join by code works.
- Refresh list works with existing cooldown behavior.
- Lobby list row click joins by lobby ID.
- Ready toggle updates button label and backend state.
- Host auto-allocation + publish server info still occurs.
- Auto-connect to allocated server still occurs.
- Reconnect behavior using stored match ID still works.
- Leave lobby and Return to Starter behavior unchanged.
- Status/error messages remain visible.

## Profile Scene Wiring Steps
1. Open `PlayerProfileScene`.
2. Ensure a `UIDocument` exists (recommended object name: `UIToolkitRoot`).
3. Assign the same runtime `PanelSettings` used by Starter/Login/Lobby.
4. Keep `PlayerProfileMenu` component; do not replace backend logic.
5. In `PlayerProfileMenu` inspector:
   - Enable `enableUiToolkit`.
   - Leave `autoAssignUiDocument`, `autoAssignVisualTreeFromResources`, and `autoAssignStylesFromResources` enabled.
6. Keep existing dependencies wired:
   - `matchRegistryClient` (or let auto-find resolve)
7. First parity run with old Profile canvas enabled, then disable old canvas after successful parity.
8. Keep `EventSystem` with `InputSystemUIInputModule`.

## Profile Parity Checklist
- Profile/account info loads and displays correctly.
- Stats text matches backend values.
- Leaderboard list renders with local player marker (`<- you`).
- History list renders with same text format.
- Empty leaderboard/history states show correct fallback text.
- Display name validation and cooldown messaging unchanged.
- Save display name updates backend and reloads profile.
- Refresh and Return buttons behave as before.
- Status/error messages remain visible.

## Game Scene Wiring Steps
1. Open `GameScene`.
2. Ensure a `UIDocument` exists (recommended object name: `UIToolkitRootGame`).
3. Assign the same runtime `PanelSettings` used in other scenes.
4. Keep `GameHudView` and `PiecePaletteView` on their existing scene objects; do not replace board/network/session controllers.
5. In `GameHudView` inspector:
   - Enable `enableUiToolkit`.
   - Leave `autoAssignUiDocument`, `autoAssignVisualTreeFromResources`, and `autoAssignStylesFromResources` enabled.
   - If multiple `UIDocument` objects exist in scene, explicitly assign the intended `uiDocument`.
6. In `PiecePaletteView` inspector:
   - Enable `enableUiToolkit`.
   - Leave `autoAssignUiDocument`, `autoAssignVisualTreeFromResources`, and `autoAssignStylesFromResources` enabled.
   - Prefer assigning the same `uiDocument` as `GameHudView` (resource: `UI/Toolkit/Game/GameHud`).
7. Keep existing dependencies wired:
   - `boardPresenter`, `sessionController`, `networkController` (`GameHudView`)
   - `catalog`, `boardPresenter`, `pieceSelection` (`PiecePaletteView`)
8. First parity run with old in-game HUD/palette canvas enabled.
9. Disable old HUD/palette canvas objects after successful parity run.
10. Keep `EventSystem` with `InputSystemUIInputModule` active.

## Game Scene Parity Checklist
- Score labels (`P1`, `P2`), round label, and turn label update exactly as before.
- `Esc` toggles in-game menu visibility while match is active.
- `Finish Round` still calls `BoardPresenter.FinishRoundForCurrentPlayer()`.
- Surrender flow unchanged:
  - `Surrender` sends request.
  - Button label switches to `OK` when acknowledgement is required.
  - Opponent surrender shows same result messaging.
- Exit flow unchanged:
  - Match key cleanup on game over.
  - Session/network shutdown behavior unchanged.
  - Scene returns to `StarterScene`.
- Piece palette list updates with remaining counts and selected state.
- Unavailable pieces are hidden exactly as before.
- Piece selection is blocked when it is not local player's turn.
- No duplicate click execution from mixed uGUI/Toolkit bindings.
- No console warnings for missing Toolkit IDs/resources.
