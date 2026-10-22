using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 앱 내보내기 — 메뉴 Tools ▸ Sevens ▸ 앱 빌드.
///
/// - Android APK: unity-client/Builds/Android/SevensGame.apk  (휴대폰에 복사해서 설치)
/// - Windows   : unity-client/Builds/Windows/SevensGame.exe
/// 빌드할 씬은 Build Settings 에 켜져 있는 것(Home, CardGame). 끝나면 결과 폴더를 연다.
/// 휴대폰에서는 노트북 서버(같은 Wi-Fi)의 IP를 오른쪽 위 '서버' 버튼에 넣어야 AI 대사가 나온다.
/// </summary>
public static class BuildApp
{
    private const string Product = "SevensGame";

    [MenuItem("Tools/Sevens/앱 빌드/Android APK", priority = 1)]
    public static void BuildAndroid()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        {
            EditorUtility.DisplayDialog("Android 빌드 모듈 없음",
                "Unity Hub ▸ 설치 ▸ 이 버전(6000.3.14f1) 톱니바퀴 ▸ 모듈 추가에서\n" +
                "'Android Build Support'(OpenJDK, Android SDK & NDK 포함)를 설치한 뒤 다시 눌러 주세요.", "확인");
            return;
        }

        var android = NamedBuildTarget.Android;
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);   // ARM64 는 IL2CPP 필요
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;          // 노트북 서버가 http 라서
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;            // 홈은 세로로 시작
        if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(android)))
            PlayerSettings.SetApplicationIdentifier(android, "com.Sevens.SevensGame");
        EditorUserBuildSettings.buildAppBundle = false;                                // .aab 말고 바로 설치되는 .apk

        Build(BuildTarget.Android, "Builds/Android/" + Product + ".apk");
    }

    [MenuItem("Tools/Sevens/앱 빌드/Windows (exe)", priority = 2)]
    public static void BuildWindows()
    {
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        Build(BuildTarget.StandaloneWindows64, "Builds/Windows/" + Product + ".exe");
    }

    private static void Build(BuildTarget target, string relPath)
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("빌드할 씬 없음", "File ▸ Build Profiles 에서 Home, CardGame 씬을 켜 주세요.", "확인");
            return;
        }
        if (!EditorSceneManagerSaveCheck()) return;

        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relPath));
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        });

        var sum = report.summary;
        if (sum.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildApp] 빌드 성공: {path} ({sum.totalSize / (1024f * 1024f):0.0} MB, {sum.totalTime.TotalMinutes:0.0}분)");
            EditorUtility.RevealInFinder(path);
        }
        else
        {
            Debug.LogError($"[BuildApp] 빌드 실패({sum.result}) — 오류 {sum.totalErrors}개. Console 창의 빨간 줄을 확인하세요.");
            EditorUtility.DisplayDialog("빌드 실패", $"오류 {sum.totalErrors}개. Console 창을 확인해 주세요.", "확인");
        }
    }

    /// <summary>저장 안 된 씬이 있으면 저장할지 묻는다(취소하면 빌드 안 함).</summary>
    private static bool EditorSceneManagerSaveCheck() =>
        UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
}
