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

        // 생성된 퍼즐 조각 목록
        private List<PuzzlePiece> generatedPieces = new List<PuzzlePiece>();

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
            
            // 배열 초기화 (-1은 빈 공간, 즉 아직 깎이지 않은 덩어리)
            for (int x = 0; x < GridSize; x++)
                for (int y = 0; y < GridSize; y++)
                    for (int z = 0; z < GridSize; z++)
                        puzzleGrid[x, y, z] = -1;

            int targetCellsPerPiece = Mathf.CeilToInt(27f / piecesCount);
            Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };

            // 핵심 퍼즐 알고리즘: 밖에서부터 한 조각씩 깎아서(역순 조립) 무조건 풀릴 수 있는 형태를 보장함
            for (int p = piecesCount - 1; p >= 1; p--)
            {
                // 방향 무작위 섞기
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector3Int temp = directions[i];
                    int rnd = Random.Range(i, directions.Length);
                    directions[i] = directions[rnd];
                    directions[rnd] = temp;
                }

                bool carved = false;
                foreach (Vector3Int dir in directions)
                {
                    List<Vector3Int> exposedCells = new List<Vector3Int>();
                    for (int x = 0; x < GridSize; x++)
                        for (int y = 0; y < GridSize; y++)
                            for (int z = 0; z < GridSize; z++)
                                if (puzzleGrid[x, y, z] == -1 && IsExposed(new Vector3Int(x, y, z), dir, -1))
                                    exposedCells.Add(new Vector3Int(x, y, z));

                    if (exposedCells.Count > 0)
                    {
                        Vector3Int seed = exposedCells[Random.Range(0, exposedCells.Count)];
                        List<Vector3Int> pieceCells = new List<Vector3Int>();
                        List<Vector3Int> activeFrontier = new List<Vector3Int>();
                        
                        pieceCells.Add(seed);
                        activeFrontier.Add(seed);
                        puzzleGrid[seed.x, seed.y, seed.z] = -2; // 현재 깎는 중인 임시 마커

                        while (activeFrontier.Count > 0 && pieceCells.Count < targetCellsPerPiece + Random.Range(-1, 2))
                        {
                            int randIdx = Random.Range(0, activeFrontier.Count);
                            Vector3Int curr = activeFrontier[randIdx];
                            bool expanded = false;

                            foreach (Vector3Int neighDir in directions)
                            {
                                Vector3Int neighbor = curr + neighDir;
                                if (neighbor.x >= 0 && neighbor.x < GridSize && neighbor.y >= 0 && neighbor.y < GridSize && neighbor.z >= 0 && neighbor.z < GridSize)
                                {
                                    if (puzzleGrid[neighbor.x, neighbor.y, neighbor.z] == -1) // 덩어리면
                                    {
                                        if (IsExposed(neighbor, dir, -1)) // 빼낼 때 걸리지 않으면
                                        {
                                            pieceCells.Add(neighbor);
                                            activeFrontier.Add(neighbor);
                                            puzzleGrid[neighbor.x, neighbor.y, neighbor.z] = -2;
                                            expanded = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            if (!expanded) activeFrontier.RemoveAt(randIdx);
                        }

                        // 완성된 조각 확정
                        foreach (Vector3Int cell in pieceCells)
                            puzzleGrid[cell.x, cell.y, cell.z] = p;

                        carved = true;
                        break;
                    }
                }
                
                if (!carved) Debug.LogWarning($"Piece {p} 조각 깎기 실패.");
            }

            // 나머지 안 깎인 덩어리는 코어 조각(Piece 0)
            for (int x = 0; x < GridSize; x++)
                for (int y = 0; y < GridSize; y++)
                    for (int z = 0; z < GridSize; z++)
                        if (puzzleGrid[x, y, z] == -1)
                            puzzleGrid[x, y, z] = 0;
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
            // 바깥에서부터 한 조각씩 깎았으므로, 풀이 순서는 가장 높은 숫자의 조각부터 풀림
            int rootPieceID = piecesCount - 1;
            generatedPieces[rootPieceID].Unlock(); // 가장 겉 조각은 처음부터 풀려있음

            // 일렬로 종속성을 부여함 (Piece 3 -> 2 -> 1 -> 0 순으로 풀리게 보장)
            for (int i = piecesCount - 2; i >= 0; i--)
            {
                PuzzlePiece childPiece = generatedPieces[i];
                PuzzlePiece parentPiece = generatedPieces[i + 1];
                
                childPiece.parentPiece = parentPiece;
                childPiece.isLocked = true; // 이전 조각이 풀리기 전까지 잠금

                parentPiece.dependentPieces.Add(childPiece);
                
                // 위치를 원점에 고정 (정확히 조립된 큐브 상태를 초기값으로 유지)
                childPiece.transform.localPosition = Vector3.zero;
                childPiece.transform.localRotation = Quaternion.identity;
                
                Debug.Log($"Piece {i} is locked by Piece {i + 1}");
            }
        }



        private bool IsExposed(Vector3Int cell, Vector3Int dir, int solidValue)
        {
            Vector3Int curr = cell + dir;
            // 지정된 방향(dir)으로 쭉 나아갔을 때 장애물(solidValue)이 있는지 검사
            while (curr.x >= 0 && curr.x < GridSize && curr.y >= 0 && curr.y < GridSize && curr.z >= 0 && curr.z < GridSize)
            {
                if (puzzleGrid[curr.x, curr.y, curr.z] == solidValue) return false;
                curr += dir;
            }
            return true;
        }
    }
}
