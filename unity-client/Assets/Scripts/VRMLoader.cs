using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UniGLTF;
using VRM;

/// <summary>
/// StreamingAssets에 있는 .vrm 파일을 런타임에 로드합니다.
/// spawnPoint를 지정하지 않으면 자신의 Transform 위치에 생성됩니다.
/// </summary>
public class VRMLoader : MonoBehaviour
{
    [SerializeField] private string    vrmFileName = "character.vrm";
    [SerializeField] private Transform spawnPoint;

    // 로드된 인스턴스를 외부에서 참조할 수 있도록 공개
    public GameObject LoadedCharacter { get; private set; }

    async void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, vrmFileName);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android에서는 StreamingAssets가 .jar 안에 있으므로 복사 필요
        path = await CopyToTemp(path, vrmFileName);
#endif

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[VRMLoader] VRM 파일 없음: {path}\n" +
                             "StreamingAssets 폴더에 .vrm 파일을 넣어주세요.");
            return;
        }

        await LoadVRM(path);
    }

    async Task LoadVRM(string path)
    {
        var parser = new GlbFileParser(path);
        var data   = parser.Parse();
        var vrmData = new VRMData(data);

        using var context  = new VRMImporterContext(vrmData);
        var       instance = await context.LoadAsync(new ImmediateCaller());

        instance.EnableUpdateWhenOffscreen();
        instance.ShowMeshes();

        var root = instance.Root;
        var t    = spawnPoint != null ? spawnPoint : transform;
        root.transform.SetPositionAndRotation(t.position, t.rotation);

        // CharacterWander / CharacterDialogue의 참조 대상을 교체
        var wander = GetComponent<CharacterWander>();
        if (wander != null)
        {
            // NavMeshAgent는 이 GameObject에 붙어 있으므로 그대로 유지
            // VRM 메시만 root에 있음
        }

        LoadedCharacter = root;
        Debug.Log($"[VRMLoader] {vrmFileName} 로드 완료");
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    async Task<string> CopyToTemp(string srcPath, string fileName)
    {
        string dest = Path.Combine(Application.temporaryCachePath, fileName);
        if (File.Exists(dest)) return dest;

        using var www = new UnityEngine.Networking.UnityWebRequest(srcPath);
        www.downloadHandler = new UnityEngine.Networking.DownloadHandlerFile(dest);
        var op = www.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        return dest;
    }
#endif
}
