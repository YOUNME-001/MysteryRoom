using UnityEngine;
using System.Collections.Generic;

namespace MysteryRoom.Puzzle
{
    /// <summary>
    /// 무작위로 캐스트 퍼즐 형태를 생성하고,
    /// 조각들 간의 종속성(풀이 순서)을 구성하는 제너레이터입니다.
    /// </summary>
    public class CastPuzzleGenerator : MonoBehaviour{
        public static CastPuzzleGenerator Instance { get; private set; }

        [Header("Generation Settings")]
        public int piecesCount = 4; // 생성할 조각의 총 개수
        public float puzzleSpreadRadius = 1.0f; // 초기 생성 시 퍼즐이 뭉쳐있는 반경

        // 생성된 퍼즐 조각 목록과 종속성 맵
        private List<PuzzlePiece> generatedPieces = new List<PuzzlePiece>();
        // 자식 -> 부모 관계로 묶임 (Key 조각이 해제되어야 Value 조각 리스트가 잠금 해제됨)
        private Dictionary<int, List<PuzzlePiece>> dependencyMap = new Dictionary<int, List<PuzzlePiece>>();

        // 그리드 크기 (3x3x3 큐브)
        private const int GridSize = 3;
        private int[,,] puzzleGrid;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        void Start()
        {
            GenerateRandomPuzzle();
        }

        public void GenerateRandomPuzzle()
        {
            foreach (Transform child in transform) { Destroy(child.gameObject); }
            generatedPieces.Clear();
            dependencyMap.Clear();

            // 1. 3x3x3 그리드를 각 퍼즐 조각 ID로 채우기 (Flood Fill 방식)
            GenerateVoxelGrid();

            // 2. 그리드 데이터 기반으로 실제 3D 오브젝트(퍼즐 조각) 스폰
            SpawnPiecesFromGrid();

            // 3. 조각들 간의 풀이 종속성(락) 트리 구성
            BuildDependencyTree();

            Debug.Log($"[CastPuzzleGenerator] {piecesCount}개의 조각이 맞물려 3x3x3 큐브를 이루는 퍼즐 생성 완료!");
        }

        private void GenerateVoxelGrid()
        {
            puzzleGrid = new int[GridSize, GridSize, GridSize];

            // 배열 초기화 (-1은 빈 공간)
            for (int x = 0; x < GridSize; x++)
                for (int y = 0; y < GridSize; y++)
                    for (int z = 0; z < GridSize; z++)
                        puzzleGrid[x, y, z] = -1;

            List<Vector3Int> unassignedCells = new List<Vector3Int>();
            for (int x = 0; x < GridSize; x++)
                for (int y = 0; y < GridSize; y++)
                    for (int z = 0; z < GridSize; z++)
                        unassignedCells.Add(new Vector3Int(x, y, z));

            // 배열을 섞어 무작위성을 부여
            for (int i = 0; i < unassignedCells.Count; i++)
            {
                Vector3Int temp = unassignedCells[i];
                int randomIndex = Random.Range(i, unassignedCells.Count);
                unassignedCells[i] = unassignedCells[randomIndex];
                unassignedCells[randomIndex] = temp;
            }

            int targetCellsPerPiece = Mathf.CeilToInt(27f / piecesCount);

            // 각 조각 ID별로 Seed(시작점)를 먼저 심습니다.
            List<Vector3Int> activeFrontiers = new List<Vector3Int>();
            for (int i = 0; i < piecesCount; i++)
            {
                if (unassignedCells.Count == 0) break;

                Vector3Int seed = unassignedCells[0];
                unassignedCells.RemoveAt(0);
                puzzleGrid[seed.x, seed.y, seed.z] = i;
                activeFrontiers.Add(seed);
            }

            // 남은 공간을 너비 우선 탐색(BFS)처럼 인접한 칸으로 무작위 확산하며 채웁니다.
            Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };

            while (unassignedCells.Count > 0 && activeFrontiers.Count > 0)
            {
                int randIdx = Random.Range(0, activeFrontiers.Count);
                Vector3Int current = activeFrontiers[randIdx];
                int currentPieceId = puzzleGrid[current.x, current.y, current.z];

                // 현재 조각이 너무 커지지 않도록 대략적인 크기 제한 (27칸을 pieceCount로 나눈 정도)
                int currentPieceSize = 0;
                foreach (int cellId in puzzleGrid) if (cellId == currentPieceId) currentPieceSize++;

                if (currentPieceSize >= targetCellsPerPiece + Random.Range(-1, 2))
                {
                    activeFrontiers.RemoveAt(randIdx);
                    continue;
                }

                // 인접한 빈 칸 하나를 찾아 현재 조각 ID로 병합
                bool foundEmptyNeighbor = false;
                foreach (var dir in directions)
                {
                    Vector3Int neighbor = current + dir;
                    if (neighbor.x >= 0 && neighbor.x < GridSize && neighbor.y >= 0 && neighbor.y < GridSize && neighbor.z >= 0 && neighbor.z < GridSize)
                    {
                        if (puzzleGrid[neighbor.x, neighbor.y, neighbor.z] == -1) // 빈칸
                        {
                            puzzleGrid[neighbor.x, neighbor.y, neighbor.z] = currentPieceId;
                            unassignedCells.Remove(neighbor);
                            activeFrontiers.Add(neighbor);
                            foundEmptyNeighbor = true;
                            break;
                        }
                    }
                }

                // 더 이상 확장할 빈 인접칸이 없다면 확산 목록에서 제거
                if (!foundEmptyNeighbor)
                {
                    activeFrontiers.RemoveAt(randIdx);
                }
            }

            // 만약 남아있는 빈 칸 고립영역이 있다면, 강제로 가장 가까운 번호로 덮어씌움 (안전장치)
            for (int x = 0; x < GridSize; x++)
            {
                for (int y = 0; y < GridSize; y++)
                {
                    for (int z = 0; z < GridSize; z++)
                    {
                        if (puzzleGrid[x, y, z] == -1)
                        {
                            // 주변 이웃 중 아무 ID나 가져와서 채움
                            foreach (var dir in directions)
                            {
                                Vector3Int neighbor = new Vector3Int(x, y, z) + dir;
                                if (neighbor.x >= 0 && neighbor.x < GridSize && neighbor.y >= 0 && neighbor.y < GridSize && neighbor.z >= 0 && neighbor.z < GridSize)
                                {
                                    if (puzzleGrid[neighbor.x, neighbor.y, neighbor.z] != -1)
                                    {
                                        puzzleGrid[x, y, z] = puzzleGrid[neighbor.x, neighbor.y, neighbor.z];
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SpawnPiecesFromGrid()
        {
            // 각 Piece ID를 담당할 부모 GameObject 생성
            GameObject[] pieceParents = new GameObject[piecesCount];
            for (int i = 0; i < piecesCount; i++)
            {
                pieceParents[i] = new GameObject($"CastPiece_{i}");
                pieceParents[i].transform.SetParent(this.transform);
                pieceParents[i].transform.localPosition = Vector3.zero;

                PuzzlePiece pieceComp = pieceParents[i].AddComponent<PuzzlePiece>();
                pieceComp.pieceID = i;
                
                generatedPieces.Add(pieceComp);
            }

            // 그리드를 순회하며 해당 좌표에 큐브를 스폰하고, 맞는 ID의 부모에게 자식으로 넣습니다.
            Vector3 centerOffset = new Vector3((GridSize - 1) / 2f, (GridSize - 1) / 2f, (GridSize - 1) / 2f);
            for (int x = 0; x < GridSize; x++)
            {
                for (int y = 0; y < GridSize; y++)
                {
                    for (int z = 0; z < GridSize; z++)
                    {
                        int pieceId = puzzleGrid[x, y, z];
                        if (pieceId >= 0 && pieceId < piecesCount)
                        {
                            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            cube.transform.SetParent(pieceParents[pieceId].transform);

                            // 3x3x3 큐브의 로컬 위치 지정 (중앙 정렬)
                            cube.transform.localPosition = new Vector3(x, y, z) - centerOffset;
                            cube.transform.localScale = Vector3.one;
                        }
                    }
                }
            }
        }

        private void BuildDependencyTree()
        {
            // ID가 0인 조각을 가장 중심(최상위 루트) 조각으로 취급
            generatedPieces[0].Unlock();
            dependencyMap.Add(0, new List<PuzzlePiece>());

            // 나머지 조각들에 대해 부모를 랜덤하게 할당
            for (int i = 1; i < piecesCount; i++)
            {
                int parentId = Random.Range(0, i);

                PuzzlePiece childPiece = generatedPieces[i];
                PuzzlePiece parentPiece = generatedPieces[parentId];

                childPiece.parentPiece = parentPiece;
                childPiece.isLocked = true; // 부모가 분리되기 전까진 락

                // 큐브 형태(Soma Cube)에서는 생성 시에는 완벽한 큐브 형태를 유지해야 하므로,
                // 강제로 위치를 옮기거나 교차 회전시키지 않습니다. 처음 형태(부모와 자식이 맞물린 블럭형태) 그대로 스폰됩니다.
                // 따라서 교차 배치, Overlap 제거 로직은 큐브 조립에는 불필요합니다.
                childPiece.transform.localPosition = Vector3.zero;
                childPiece.transform.localRotation = Quaternion.identity;

                if (!dependencyMap.ContainsKey(parentId))
                {
                    dependencyMap.Add(parentId, new List<PuzzlePiece>());
                }
                dependencyMap[parentId].Add(childPiece);

                Debug.Log($"Piece {i} is locked by Piece {parentId}");
            }
        }

        public void NotifyPieceSolved(int solvedPieceId)
        {
            // 특정 조각이 풀렸을 때, 그 조각에 묶여있던 자식 조각들을 풀어줌
            if (dependencyMap.ContainsKey(solvedPieceId))
            {
                List<PuzzlePiece> dependentPieces = dependencyMap[solvedPieceId];
                foreach (var piece in dependentPieces)
                {
                    piece.Unlock();
                    Debug.Log($"Piece {piece.pieceID} is now unlocked because {solvedPieceId} was solved!");
                }
            }
        }
    }
}
