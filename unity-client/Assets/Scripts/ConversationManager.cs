using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1)]
public class ConversationManager : MonoBehaviour
{
    public static ConversationManager Instance { get; private set; }

    [SerializeField] private float conversationDistance  = 3f;
    [SerializeField] private float checkInterval         = 8f;
    [SerializeField] private int   turnsPerConversation  = 3;
    [SerializeField] private float conversationCooldown  = 30f;

    private readonly List<CharacterDialogue> characters = new();
    private bool  conversationActive;
    private float lastConversationTime = -999f;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(ConversationCheckLoop());

    public void Register(CharacterDialogue c) => characters.Add(c);

    IEnumerator ConversationCheckLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (conversationActive) continue;
            if (Time.time - lastConversationTime < conversationCooldown) continue;
            if (characters.Count < 2) continue;

            var pair = FindNearbyPair();
            if (pair.HasValue)
                StartCoroutine(RunConversation(pair.Value.a, pair.Value.b));
        }
    }

    (CharacterDialogue a, CharacterDialogue b)? FindNearbyPair()
    {
        for (int i = 0; i < characters.Count; i++)
        for (int j = i + 1; j < characters.Count; j++)
        {
            float dist = Vector3.Distance(
                characters[i].transform.position,
                characters[j].transform.position);
            if (dist <= conversationDistance)
                return (characters[i], characters[j]);
        }
        return null;
    }

    IEnumerator RunConversation(CharacterDialogue a, CharacterDialogue b)
    {
        conversationActive    = true;
        a.InConversation = b.InConversation = true;

        var wanderA = a.GetComponent<CharacterWander>();
        var wanderB = b.GetComponent<CharacterWander>();
        wanderA?.Pause();
        wanderB?.Pause();

        yield return ServerClient.Instance.SetActiveCharacters(
            new[] { a.characterName, b.characterName });
        yield return ServerClient.Instance.ClearConversation();

        for (int i = 0; i < turnsPerConversation; i++)
        {
            string speaker = null, line = null;
            yield return ServerClient.Instance.GetConversationNext(
                (s, l) => { speaker = s; line = l; });

            if (string.IsNullOrEmpty(line)) continue;

            var talker = (a.characterName == speaker) ? a : b;
            var other  = (talker == a) ? b : a;

            FaceToward(talker.transform, other.transform.position);
            yield return talker.SayLine(line);
        }

        wanderA?.Resume();
        wanderB?.Resume();
        a.InConversation = b.InConversation = false;
        lastConversationTime = Time.time;
        conversationActive   = false;
    }

    static void FaceToward(Transform t, Vector3 target)
    {
        Vector3 dir = target - t.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
            t.rotation = Quaternion.LookRotation(dir);
    }
}
