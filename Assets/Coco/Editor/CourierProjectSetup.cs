using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CocoCourier.Editor
{
    [InitializeOnLoad]
    public static class CourierProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/CocoCourier.unity";
        public const string MenuPath = "Assets/Scenes/MainMenu.unity";
        static CourierProjectSetup() { EditorApplication.delayCall += EnsureScene; }
        private static void EnsureScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(ScenePath) && !File.Exists(MenuPath) && !SceneManager.GetActiveScene().isDirty) { BuildMenu(); return; }
            if (File.Exists(ScenePath))
            {
                if (SceneManager.GetActiveScene().name == "SampleScene" && !SceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            // Only replace the pristine template in the editor. Never discard an unsaved scene.
            if (SceneManager.GetActiveScene().isDirty) return;
            BuildScene();
        }
        [MenuItem("Coco Courier/Create or Open Main Menu")]
        public static void BuildMenu()
        {
            if (!File.Exists(ScenePath)) BuildScene();
            if (!File.Exists(MenuPath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Menu Camera", typeof(Camera)); camera.tag = "MainCamera";
                camera.GetComponent<Camera>().orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
                new GameObject("Coco Main Menu", typeof(CourierMenu));
                EditorSceneManager.SaveScene(scene, MenuPath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuPath, true), new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets(); EditorSceneManager.OpenScene(MenuPath);
        }
        [MenuItem("Coco Courier/Create or Open Game Scene")]
        public static void BuildScene()
        {

            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var settings = AssetDatabase.LoadAssetAtPath<CourierSettings>("Assets/Coco/Resources/CourierSettings.asset");
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<CourierSettings>();
                AssetDatabase.CreateAsset(settings, "Assets/Coco/Resources/CourierSettings.asset");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Game Camera", typeof(Camera)); camera.tag = "MainCamera";
            camera.GetComponent<Camera>().orthographic = true; camera.transform.position = new Vector3(0, 2, -10);
            var game = new GameObject("Coco Courier — Play to start", typeof(CourierGame)); game.GetComponent<CourierGame>().settings = settings;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false; PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.defaultScreenWidth = 540; PlayerSettings.defaultScreenHeight = 960;
            AssetDatabase.SaveAssets();
        }
    }

    public static class CourierVerification
    {
        private static int assertions;
        private static double deadline;
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); assertions++; Debug.Log("COCO PASS: " + message); }
        public static void RunBatch()
        {
            try
            {
                CourierProjectSetup.BuildScene();
                var settings = ScriptableObject.CreateInstance<CourierSettings>();
                var run = new CourierRun(settings, 7, false);
                Check(run.Progress == 0 && !run.Jumping && run.GroundY == 0, "Starts on island zero");
                run.Tick(2); Check(run.Elapsed > 1.99f && run.Progress == 0, "Waiting counts toward delivery time");
                Check(run.Jump() && !run.Jump(), "Exactly one jump accepted; midair input ignored");
                run.Tick(settings.jumpDuration / 2); Check(run.Jumping && run.Progress == 0, "No progress before landing");
                run.Tick(settings.jumpDuration / 2 + 0.001f); Check(!run.Jumping && run.Progress == 1, "Landing advances one island");
                for (int i = 1; i < 10; i++) { run.Jump(); run.Tick(settings.jumpDuration + 0.01f); }
                Check(run.Progress == 10 && run.State == CourierRun.RunState.Delivered, "Ten jumps deliver the order");
                float finish = run.Elapsed; run.Tick(10); Check(run.Elapsed == finish && !run.Jump(), "Result freezes timer and movement");
                Check(run.Rating == 10, "Fast delivery earns ten points");
                run = new CourierRun(settings, 3, false); run.Tick(100);
                for (int i = 0; i < 10; i++) { run.Jump(); run.Tick(1); }
                Check(run.Rating == 1, "Slow delivery earns one point, no timeout");
                run = new CourierRun(settings, 4, false);
                run.Cars.Add(new CourierRun.Car { lane = 0, x = 0, speed = 0 });
                run.Tick(1); Check(run.State == CourierRun.RunState.Playing, "Island is safe beside occupied lane");
                run.Jump(); run.Tick(settings.jumpDuration / 2);
                Check(run.State == CourierRun.RunState.Crashed && run.Progress == 0 && run.Jumping, "Collision during flight despite visual arc");
                Check(CourierRun.SweptHit(new Vector2(-8, 0), new Vector2(8, 0), Vector2.one), "Swept collision catches fast crossing between frames");
                run = new CourierRun(settings, 9);
                Check(run.Cars.Count == 10, "Exactly ten traffic lanes");
                Check(run.Cars[0].speed > 0 && run.Cars[1].speed < 0 && Mathf.Abs(run.Cars[9].speed) > Mathf.Abs(run.Cars[0].speed), "Alternating traffic with varied speeds");
                run.Tick(20); Check(run.State == CourierRun.RunState.Playing && run.Cars.Count > 0, "Traffic continues while waiting on safe island");
                VerifyEconomy(settings);
                UnityEngine.Object.DestroyImmediate(settings);
                File.WriteAllText("Logs/CocoVerification.txt", assertions + " simulation checks passed. Entering Play Mode.\n");
                deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.update += VerifyPlay;
                SessionState.SetBool("CocoVerifyRunning", true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Fail(error); }
        }
        // SessionState survives the domain reload when entering Play Mode.
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!SessionState.GetBool("CocoVerifyRunning", false)) return;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += VerifyPlay;
        }
        private static int stage;
        private static void VerifyPlay()
        {
            SessionState.SetBool("CocoVerifyRunning", true);
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Play Mode verification timed out");
                if (!EditorApplication.isPlaying) return;
                var game = UnityEngine.Object.FindFirstObjectByType<CourierGame>();
                if (!game || game.Run == null) return;
                if (stage == 0)
                {
                    Check(UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null, "Play Mode creates UI input");
                    Check(GameObject.Find("Portrait HUD") != null && GameObject.Find("Lane 10") != null, "Play Mode creates HUD and ten lanes");
                    SessionState.SetBool("CocoOldEconomyExists", PlayerPrefs.HasKey(CourierEconomy.SaveKey));
                    SessionState.SetString("CocoOldEconomy", PlayerPrefs.GetString(CourierEconomy.SaveKey, ""));
                    SessionState.SetString("CocoOldBalance", game.Wallet.Balance.ToString());
                    SessionState.SetString("CocoOldEarned", game.Wallet.TotalEarned.ToString());
                    SessionState.SetInt("CocoOldCount", CourierRating.Count);
                    SessionState.SetInt("CocoOldSum", PlayerPrefs.GetInt("CocoCourier.RatingSum.v1", 0));
                    SessionState.SetBool("CocoPrefsSaved", true);
                    game.Run.Cars.Clear(); game.Run.Cars.Add(new CourierRun.Car { lane = 0, x = 0, speed = 0 });
                    game.Jump(); game.Run.Tick(0.4f); stage = 1; return;
                }
                if (stage == 1)
                {
                    Check(GameObject.Find("Result overlay") != null && game.Run.State == CourierRun.RunState.Crashed, "Crash result is displayed in Play Mode");
                    Check(game.Wallet.Balance == long.Parse(SessionState.GetString("CocoOldBalance", "0")), "Crash does not award or deduct coins");
                    CapturePortrait(game, "Logs/CocoCrash.png");
                    GameObject.Find("Retry").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    Check(game.Run.Progress == 0 && game.Run.State == CourierRun.RunState.Playing, "Retry button starts fresh attempt");
                    // Preserve the user's career data while verifying the real delivery result/persistence path.
                    SessionState.SetInt("CocoOldCount", PlayerPrefs.GetInt("CocoCourier.DeliveryCount.v1", 0));
                    SessionState.SetInt("CocoOldSum", PlayerPrefs.GetInt("CocoCourier.RatingSum.v1", 0));
                    SessionState.SetBool("CocoPrefsSaved", true);
                    for (int i = 0; i < 10; i++) { game.Run.Cars.Clear(); game.Jump(); game.Run.Tick(game.settings.jumpDuration + 0.001f); }
                    stage = 2; return;
                }
                if (stage == 2)
                {
                    Check(game.Run.State == CourierRun.RunState.Delivered && GameObject.Find("Result overlay") != null, "Delivery result is displayed in Play Mode");
                    Check(CourierRating.Count == SessionState.GetInt("CocoOldCount", 0) + 1, "Successful rating persisted once");
                    Check(game.Wallet.Balance == long.Parse(SessionState.GetString("CocoOldBalance", "0")) + 100, "Delivery adds 100 coins to wallet");
                    Check(game.Wallet.TotalEarned == long.Parse(SessionState.GetString("CocoOldEarned", "0")) + 100, "Lifetime earnings increase with delivery");
                    Check(!game.Wallet.TryReward(game.Run, out _), "Repeated result cannot pay twice");
                    Check(new CourierEconomy().Balance == game.Wallet.Balance, "Wallet survives save reload");
                    Check(GameObject.Find("Reward breakdown").GetComponent<UnityEngine.UI.Text>().text.Contains("+100 COINS"), "Result displays reward breakdown");
                    CapturePortrait(game, "Logs/CocoDelivery.png");
                    GameObject.Find("Retry").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    Check(game.Run.Progress == 0 && !game.Run.Jumping, "Retry works after delivery");
                    Check(game.Wallet.Balance == long.Parse(SessionState.GetString("CocoOldBalance", "0")) + 100, "Retry keeps earned coins without another payment");
                    CapturePortrait(game);
                    game.Run.Cars.Clear(); game.Run.Cars.Add(new CourierRun.Car { lane = 0, x = 0, speed = 0 });
                    game.Jump(); game.Run.Tick(0.4f); stage = 3; return;
                }
                if (stage == 3)
                {
                    Check(game.Run.State == CourierRun.RunState.Crashed && game.Wallet.Balance == long.Parse(SessionState.GetString("CocoOldBalance", "0")) + 100, "Later collision preserves earned balance");
                    Check(CourierRating.Count == SessionState.GetInt("CocoOldCount", 0) + 1, "Later result never duplicates delivery rating");
                    File.AppendAllText("Logs/CocoVerification.txt", assertions + " Play Mode checks passed: crash, delivery, wallet, persistence, duplicate protection, retry.\n");
                    RestorePrefs();
                    SessionState.SetBool("CocoVerifyRunning", false);
                    EditorApplication.update -= VerifyPlay; EditorApplication.Exit(0);
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static void VerifyEconomy(CourierSettings settings)
        {
            bool existed = PlayerPrefs.HasKey(CourierEconomy.SaveKey);
            string original = PlayerPrefs.GetString(CourierEconomy.SaveKey, "");
            try
            {
                PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);
                var wallet = new CourierEconomy();
                Check(wallet.Balance == 0 && wallet.TotalEarned == 0, "Existing players start with zero coins without retroactive rewards");
                Check(OrderReward.ForRating(1).Total == 55 && OrderReward.ForRating(5).Total == 75 && OrderReward.ForRating(10).Total == 100, "Rewards match 50 + rating x 5");
                var run = new CourierRun(settings, 1, false);
                Check(!wallet.TryReward(run, out _), "Starting an order grants no coins");
                for (int i = 0; i < 9; i++) { run.Jump(); run.Tick(settings.jumpDuration + 0.001f); }
                Check(!wallet.TryReward(run, out _) && wallet.Balance == 0, "Nine lanes grant no partial reward");
                run.Jump(); run.Tick(settings.jumpDuration + 0.001f);
                Check(wallet.TryReward(run, out var reward) && reward.BaseCoins == 50 && reward.RatingBonus == 50 && wallet.Balance == 100, "Tenth landing pays the whole order");
                Check(!wallet.TryReward(run, out _) && wallet.Balance == 100, "Same order cannot be paid twice");
                wallet = new CourierEconomy();
                Check(wallet.Balance == 100 && wallet.TotalEarned == 100 && !wallet.TryReward(run, out _), "Reload preserves balance, earnings and duplicate protection");
                var slow = new CourierRun(settings, 2, false); slow.Tick(100);
                for (int i = 0; i < 10; i++) { slow.Jump(); slow.Tick(settings.jumpDuration + 0.001f); }
                Check(wallet.TryReward(slow, out reward) && reward.Total == 55 && wallet.Balance == 155 && wallet.TotalEarned == 155, "Another slow delivery adds 55 coins");
                Check(run.OrderId != slow.OrderId && !wallet.TryReward(run, out _), "Older paid order stays rejected after a newer order");
                var crashed = new CourierRun(settings, 3, false);
                crashed.Cars.Add(new CourierRun.Car { lane = 0, x = 0, speed = 0 }); crashed.Jump(); crashed.Tick(0.4f);
                Check(!wallet.TryReward(crashed, out _) && wallet.Balance == 155, "Collision preserves savings and grants zero coins");
            }
            finally
            {
                if (existed) PlayerPrefs.SetString(CourierEconomy.SaveKey, original); else PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);
                PlayerPrefs.Save();
            }
        }
        private static void CapturePortrait(CourierGame game, string path = "Logs/CocoPortrait.png")
        {
            var camera = Camera.main;
            var canvas = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Canvas>();
            var target = new RenderTexture(540, 960, 24);
            var pixels = new Texture2D(540, 960, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1; canvas.sortingOrder = 1000;
                game.SendMessage("UpdatePresentation");
                Canvas.ForceUpdateCanvases();
                var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 0;
                UnityEngine.Object.DestroyImmediate(pixels); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        private static void RestorePrefs()
        {
            if (!SessionState.GetBool("CocoPrefsSaved", false)) return;
            if (SessionState.GetBool("CocoOldEconomyExists", false)) PlayerPrefs.SetString(CourierEconomy.SaveKey, SessionState.GetString("CocoOldEconomy", ""));
            else PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);
            PlayerPrefs.SetInt("CocoCourier.DeliveryCount.v1", SessionState.GetInt("CocoOldCount", 0));
            PlayerPrefs.SetInt("CocoCourier.RatingSum.v1", SessionState.GetInt("CocoOldSum", 0)); PlayerPrefs.Save();
            SessionState.SetBool("CocoPrefsSaved", false);
        }
        private static void Fail(Exception error)
        {
            RestorePrefs(); SessionState.SetBool("CocoVerifyRunning", false);
            Debug.LogException(error); File.AppendAllText("Logs/CocoVerification.txt", "FAILED: " + error + "\n"); EditorApplication.Exit(1);
        }
    }
}








