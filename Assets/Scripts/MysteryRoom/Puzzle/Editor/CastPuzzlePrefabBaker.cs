#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace MysteryRoom.Puzzle.Editor
{
    /// <summary>
    /// 동적으로 생성된 캐스트 퍼즐을 머티리얼 깨짐 없이 완전한 프리팹으로 구워주는 에디터 유틸리티입니다.
    /// 마우스 우클릭 컨텍스트 메뉴를 통해 사용할 수 있습니다.
    /// </summary>
    public static class CastPuzzlePrefabBaker
    {
        [MenuItem("GameObject/MysteryRoom/Bake Cast Puzzle to Prefab", false, 0)]
        public static void BakeToPrefab(MenuCommand menuCommand)
        {
            GameObject selectedObj = menuCommand.context as GameObject;

            if (selectedObj == null)
            {
                EditorUtility.DisplayDialog("Error", "퍼즐 오브젝트를 선택해주세요.", "OK");
                return;
            }

            // 루트 타겟 폴더
            string rootFolderPath = "Assets/Prefabs/GeneratedPuzzles";
            if (!AssetDatabase.IsValidFolder(rootFolderPath))
            {
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs/GeneratedPuzzles"));
                AssetDatabase.Refresh();
            }

            // 현재 날짜/시간(예: 20260317_153022)으로 서브 폴더 생성
            string timeStamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string targetFolderPath = $"{rootFolderPath}/{timeStamp}";
            
            if (!AssetDatabase.IsValidFolder(targetFolderPath))
            {
                AssetDatabase.CreateFolder(rootFolderPath, timeStamp);
            }

            // 1차적으로 씬에 있는 동적 머티리얼들을 생성된 날짜 서브 폴더 안에 추출하여 저장
            Renderer[] renderers = selectedObj.GetComponentsInChildren<Renderer>(true);
            int matIndex = 0;
            
            // 이미 파일로 추출(Save)이 끝난 동적 머티리얼을 추적해 중복 생성을 완벽 차단
            System.Collections.Generic.Dictionary<Material, Material> savedMaterialsDict = new System.Collections.Generic.Dictionary<Material, Material>();
            
            foreach (Renderer rend in renderers)
            {
                if (rend.sharedMaterial != null && !AssetDatabase.Contains(rend.sharedMaterial))
                {
                    if (savedMaterialsDict.TryGetValue(rend.sharedMaterial, out Material savedMat))
                    {
                        // 이미 앞선 큐브가 구워놓은 머티리얼 에셋 재사용! (머티리얼 갯수를 전체 큐브 수 -> 조각 수로 획기적 감축)
                        rend.sharedMaterial = savedMat;
                    }
                    else
                    {
                        // 아직 구워지지 않은 동적 머티리얼이면 새로 에셋 생성 및 딕셔너리에 등록
                        Material matClone = new Material(rend.sharedMaterial);
                        string matPath = $"{targetFolderPath}/PuzzleMat_ID{matIndex++}.mat";
                        
                        AssetDatabase.CreateAsset(matClone, matPath);
                        Material newSavedAsset = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        
                        savedMaterialsDict[rend.sharedMaterial] = newSavedAsset;
                        rend.sharedMaterial = newSavedAsset;
                    }
                }
            }
            
            // 모든 머티리얼 저장이 완료되면 AssetDatabase 갱신
            AssetDatabase.SaveAssets();

            // 핵심 버그 픽스: 프리팹으로 구울 때 CastPuzzleGenerator 컴포넌트 강제 삭제.
            // (저장된 프리팹이 플레이 시 다시 퍼즐을 자동 스폰하여 겹쳐서 폭발하는 물리 버그 방지)
            CastPuzzleGenerator generator = selectedObj.GetComponent<CastPuzzleGenerator>();
            if (generator != null)
            {
                UnityEngine.Object.DestroyImmediate(generator);
            }

            // 프리팹에는 런타임 퍼즐 컴플리션 체크 매니저 기능만 남깁니다.
            CastPuzzleManager manager = selectedObj.GetComponent<CastPuzzleManager>();
            if (manager == null)
            {
                selectedObj.AddComponent<CastPuzzleManager>();
            }

            // 2차적으로 프리팹 생성 (해당 폴더 안에)
            string prefabPath = $"{targetFolderPath}/{selectedObj.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(selectedObj, prefabPath, InteractionMode.UserAction);

            if (prefab != null)
            {
                EditorUtility.DisplayDialog("Success", $"새로운 날짜 폴더에 프리팹이 성공적으로 저장되었습니다!\n경로: {prefabPath}", "OK");
                Selection.activeObject = prefab;
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "프리팹 저장에 실패했습니다.", "OK");
            }
        }
    }
}
#endif
