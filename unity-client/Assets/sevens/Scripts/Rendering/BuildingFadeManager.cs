using UnityEngine;
using System.Collections.Generic;

namespace WorldSpaceTransitions
{
    /// <summary>
    /// 카메라와 플레이어 사이에 있는 건물을 디더 페이드로 반투명 처리
    ///
    /// 방식: MaterialPropertyBlock으로 개별 Renderer의 _FadeAlpha를 조절
    /// - 글로벌 셰이더 키워드(FADE_SPHERE) 사용하지 않음
    /// - CyberpunkNeon 셰이더의 디더 클립과 연동
    /// - 시야를 가리지 않는 건물은 영향 없음 (기본 _FadeAlpha = 1)
    /// </summary>
    public class BuildingFadeManager : MonoBehaviour
    {
        [Header("타겟 설정")]
        public string playerTag = "Player";

        [Header("페이드 설정")]
        public bool enableCameraFade = true;

        [Tooltip("페이드 최대량 (1 = 완전 투명, 0.85 = 대부분 투명)")]
        [Range(0.3f, 1f)]
        public float maxFadeAmount = 0.85f;

        [Tooltip("페이드 전환 속도")]
        [Range(1f, 20f)]
        public float fadeSpeed = 8f;

        [Header("레이캐스트 설정")]
        [Tooltip("레이캐스트 구체 반경")]
        public float raycastRadius = 1.0f;

        [Tooltip("건물 레이어 마스크")]
        public LayerMask buildingLayer = ~0;

        [Tooltip("레이캐스트 업데이트 주기")]
        public float updateInterval = 0.1f;

        [Tooltip("무시할 최소 오브젝트 높이")]
        public float minBuildingHeight = 1.5f;

        private Transform player;
        private Camera mainCamera;
        private float nextUpdateTime;

        // Renderer별 현재 페이드 상태 추적
        private Dictionary<Renderer, float> fadingRenderers = new Dictionary<Renderer, float>();
        // 이번 프레임에 차단 감지된 Renderer
        private HashSet<Renderer> currentBlockers = new HashSet<Renderer>();

        private static readonly int FadeAmountID = Shader.PropertyToID("_FadeAmount");
        private MaterialPropertyBlock mpb;

        void Start()
        {
            mainCamera = Camera.main;
            mpb = new MaterialPropertyBlock();
            FindPlayer();
        }

        void FindPlayer()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null)
                player = playerObj.transform;
        }

        void Update()
        {
            if (player == null)
            {
                FindPlayer();
                if (player == null) return;
            }
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }

            // 주기적으로 차단 건물 감지
            if (Time.time >= nextUpdateTime)
            {
                nextUpdateTime = Time.time + updateInterval;
                DetectBlockingBuildings();
            }

            // 매 프레임 페이드 보간
            UpdateFadeValues();
        }

        void DetectBlockingBuildings()
        {
            currentBlockers.Clear();

            if (!enableCameraFade) return;

            Vector3 camPos = mainCamera.transform.position;
            Vector3 playerPos = player.position + Vector3.up * 1.5f;
            Vector3 direction = playerPos - camPos;
            float distance = direction.magnitude;

            if (distance < 0.5f) return;

            // 카메라 → 플레이어 SphereCast
            RaycastHit[] hits = Physics.SphereCastAll(
                camPos, raycastRadius, direction.normalized,
                distance * 0.9f, buildingLayer
            );

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform == player) continue;
                if (hit.transform.IsChildOf(player)) continue;

                Renderer renderer = hit.collider.GetComponent<Renderer>();
                if (renderer == null)
                    renderer = hit.collider.GetComponentInParent<Renderer>();
                if (renderer == null) continue;

                // 작은 오브젝트 스킵
                Bounds bounds = renderer.bounds;
                if (bounds.size.y < minBuildingHeight) continue;

                // CyberpunkNeon 셰이더인지 확인
                if (renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.shader.name == "Sevens/CyberpunkNeon")
                {
                    currentBlockers.Add(renderer);

                    // 새로 감지된 Renderer면 추적 시작
                    if (!fadingRenderers.ContainsKey(renderer))
                    {
                        fadingRenderers[renderer] = 0f; // 현재 페이드 없음 (불투명)
                    }
                }
            }
        }

        void UpdateFadeValues()
        {
            List<Renderer> toRemove = new List<Renderer>();

            // 기존 추적 중인 모든 Renderer 업데이트
            List<Renderer> keys = new List<Renderer>(fadingRenderers.Keys);
            foreach (Renderer rend in keys)
            {
                if (rend == null)
                {
                    toRemove.Add(rend);
                    continue;
                }

                float current = fadingRenderers[rend];
                float target;

                if (currentBlockers.Contains(rend))
                {
                    // 차단 중 → 페이드 증가 (투명하게)
                    target = maxFadeAmount;
                }
                else
                {
                    // 더 이상 차단 안 함 → 페이드 감소 (불투명으로 복원)
                    target = 0f;
                }

                float newValue = Mathf.MoveTowards(current, target, Time.deltaTime * fadeSpeed);
                fadingRenderers[rend] = newValue;

                // MaterialPropertyBlock 적용
                rend.GetPropertyBlock(mpb);
                mpb.SetFloat(FadeAmountID, newValue);
                rend.SetPropertyBlock(mpb);

                // 완전 복원된 Renderer는 추적 해제
                if (newValue <= 0.001f && !currentBlockers.Contains(rend))
                {
                    // PropertyBlock 초기화 (페이드 없음)
                    rend.GetPropertyBlock(mpb);
                    mpb.SetFloat(FadeAmountID, 0f);
                    rend.SetPropertyBlock(mpb);
                    toRemove.Add(rend);
                }
            }

            foreach (Renderer rend in toRemove)
            {
                fadingRenderers.Remove(rend);
            }
        }

        void OnDisable()
        {
            RestoreAllRenderers();
        }

        void OnDestroy()
        {
            RestoreAllRenderers();
        }

        /// <summary>
        /// 모든 페이드된 Renderer를 원래 상태로 복원
        /// </summary>
        void RestoreAllRenderers()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();

            foreach (var kvp in fadingRenderers)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.GetPropertyBlock(mpb);
                    mpb.SetFloat(FadeAmountID, 0f);
                    kvp.Key.SetPropertyBlock(mpb);
                }
            }
            fadingRenderers.Clear();
        }
    }
}
