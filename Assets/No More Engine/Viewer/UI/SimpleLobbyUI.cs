using NoMoreEngine.Session;
using UnityEngine;


namespace NoMoreEngine.Viewer.UI
{
    /// <summary>
    /// Simple UI visualization for lobby states
    /// Temporary OnGUI implementation for testing
    /// </summary>
    public class SimpleLobbyUI : MonoBehaviour
    {
        private SessionCoordinator coordinator;

        void Start()
        {
            coordinator = SessionCoordinator.Instance;
            if (coordinator == null)
            {
                UnityEngine.Debug.LogError("[SimpleLobbyUI] SessionCoordinator not found!");
                enabled = false;
            }
        }

        void OnGUI()
        {
            // Only show UI in lobby states
            var currentStateType = coordinator.GetCurrentStateType();

            switch (currentStateType)
            {
                case SessionStateType.MainMenu:
                    DrawMainMenu();
                    break;

                case SessionStateType.MissionLobby:
                    DrawMissionLobby();
                    break;

                case SessionStateType.Results:
                    DrawResults();
                    break;
                case SessionStateType.ReplayMode:
                    DrawReplayMode();
                    break;
            }
        }

        private void DrawMainMenu()
        {
            GUILayout.BeginArea(new Rect(Screen.width/2 - 200, Screen.height/2 - 150, 400, 300));
            GUILayout.BeginVertical("box");

            GUILayout.Label("MAIN MENU", GetTitleStyle());
            GUILayout.Space(20);

            if (GUILayout.Button("Mission Mode", GetButtonStyle(), GUILayout.Height(50)))
            {
                coordinator.TransitionTo(SessionStateType.MissionLobby);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Replay Mode (X/E)", GetButtonStyle(), GUILayout.Height(50)))
            {
                coordinator.TransitionTo(SessionStateType.ReplayMode);
            }

            GUILayout.Space(10);

            GUI.enabled = false;
            if (GUILayout.Button("Versus Mode (Coming Soon)", GetButtonStyle(), GUILayout.Height(50)))
            {
                // TODO
            }
            GUI.enabled = true;

            GUILayout.Space(20);
            GUILayout.Label("Press SPACE/A or ENTER to quick start", GetLabelStyle());

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawMissionLobby()
        {
            var gameConfig = coordinator.GetGameConfig();
            var currentState = coordinator.GetCurrentState() as MissionLobbyState;

            GUILayout.BeginArea(new Rect(Screen.width/2 - 200, Screen.height/2 - 250, 400, 500));
            GUILayout.BeginVertical("box");

            GUILayout.Label("MISSION LOBBY", GetTitleStyle());
            GUILayout.Space(20);

            // Stage selection
            GUILayout.Label("Stage Selection:", GetLabelStyle());
            GUILayout.BeginVertical("box");

            string[] stages = { "TestArena", "SmallBox", "LargePlatform" };
            foreach (var stage in stages)
            {
                bool isSelected = (gameConfig.stageName == stage);

                GUI.color = isSelected ? Color.cyan : Color.white;
                GUILayout.Label($"{(isSelected ? "> " : "  ")}{stage}", GetLabelStyle());
                GUI.color = Color.white;
            }

            GUILayout.EndVertical();
            GUILayout.Space(20);

            // Player info
            GUILayout.Label($"Players: {gameConfig.GetActivePlayerCount()}", GetLabelStyle());
            GUILayout.Label($"Mode: {gameConfig.gameMode}", GetLabelStyle());

            GUILayout.Space(20);

            // Countdown display
            if (currentState != null && currentState.IsCountingDown)
            {
                float countdown = currentState.GetCountdown();
                GUI.color = Color.yellow;
                GUILayout.Label($"Starting in {countdown:F1}...", GetTitleStyle());
                GUILayout.Label("Press any button to cancel", GetLabelStyle());
                GUI.color = Color.white;
            }
            else
            {
                // Instructions
                GUILayout.Label("Controls:", GetLabelStyle());
                GUILayout.Label("• UP/DOWN: Select Stage", GetLabelStyle());
                GUILayout.Label("• SPACE/A or ENTER: Start Mission", GetLabelStyle());
                GUILayout.Label("• F/B: Back to Menu", GetLabelStyle());
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawResults()
        {
            var resultsState = coordinator.GetState<ResultsState>();

            UnityEngine.Debug.Log($"[SimpleLobbyUI] Drawing results - ResultsState exists: {resultsState != null}");
            if (resultsState != null)
            {
                UnityEngine.Debug.Log($"[SimpleLobbyUI] HasPendingRecording: {resultsState.HasPendingRecording}");
            }

            GUILayout.BeginArea(new Rect(Screen.width/2 - 200, Screen.height/2 - 150, 400, 300));
            GUILayout.BeginVertical("box");

            GUILayout.Label("MISSION COMPLETE", GetTitleStyle());
            GUILayout.Space(20);

            // Show save replay prompt if we have a recording
            if (resultsState != null && resultsState.HasPendingRecording)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("SAVE REPLAY?", GetTitleStyle());
                GUI.color = Color.white;

                GUILayout.Label($"Recording: {resultsState.PendingRecordingFrames} frames", GetLabelStyle());
                GUILayout.Label($"Duration: {resultsState.PendingRecordingDuration:F1} seconds", GetLabelStyle());

                GUILayout.Space(20);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save (A/Space)", GetButtonStyle()))
                {
                    // Input handling is in ResultsState
                }
                if (GUILayout.Button("Discard (B/F)", GetButtonStyle()))
                {
                    // Input handling is in ResultsState
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Press any button to continue", GetLabelStyle());
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawReplayMode()
        {
            var replayState = coordinator.GetState<ReplayModeState>();
            if (replayState == null) return;
            
            GUILayout.BeginArea(new Rect(Screen.width/2 - 300, Screen.height/2 - 250, 600, 500));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label("REPLAY MODE", GetTitleStyle());
            GUILayout.Space(20);
            
            // Mode indicator
            GUI.color = Color.cyan;
            GUILayout.Label($"Mode: {replayState.CurrentMode}", GetLabelStyle());
            GUI.color = Color.white;
            GUILayout.Label("Press X/E to change mode", GetLabelStyle());
            GUILayout.Space(10);
            
            // Replay list
            var replays = replayState.AvailableReplays;
            if (replays == null || replays.Length == 0)
            {
                GUILayout.Label("No replays found", GetLabelStyle());
            }
            else
            {
                GUILayout.Label($"Available Replays ({replays.Length}):", GetLabelStyle());
                GUILayout.BeginVertical("box");
                
                for (int i = 0; i < replays.Length; i++)
                {
                    bool isSelected = i == replayState.SelectedIndex;
                    GUI.color = isSelected ? Color.cyan : Color.white;
                    GUILayout.Label($"{(isSelected ? "> " : "  ")}{replays[i]}", GetLabelStyle());
                }
                GUI.color = Color.white;
                
                GUILayout.EndVertical();
                
                // Replay info
                var recording = replayState.LoadedRecording;
                if (recording != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label($"Frames: {recording.FrameCount}", GetLabelStyle());
                    GUILayout.Label($"Duration: {recording.DurationSeconds:F1}s", GetLabelStyle());
                    GUILayout.Label($"Date: {recording.recordDate:g}", GetLabelStyle());
                }
            }
            
            GUILayout.Space(20);
            
            // Controls
            GUILayout.Label("Controls:", GetLabelStyle());
            GUILayout.Label("• UP/DOWN: Select Replay", GetLabelStyle());
            GUILayout.Label("• X/E: Change Test Mode", GetLabelStyle());
            GUILayout.Label("• SPACE/A: Start Test", GetLabelStyle());
            GUILayout.Label("• F/B: Back to Menu", GetLabelStyle());
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // UI Styles
        private GUIStyle GetTitleStyle()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 24;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            return style;
        }

        private GUIStyle GetButtonStyle()
        {
            var style = new GUIStyle(GUI.skin.button);
            style.fontSize = 16;
            return style;
        }

        private GUIStyle GetLabelStyle()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 14;
            return style;
        }
    }
}