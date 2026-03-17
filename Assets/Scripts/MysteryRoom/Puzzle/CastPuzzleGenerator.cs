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
        [Tooltip("The dimension of the puzzle (e.g. 3 for a 3x3x3 cube)")]
        public int gridSize = 3;
        public int piecesCount = 4; // 생성할 조각의 총 개수

        // 생성된 퍼즐 조각 목록
        private List<PuzzlePiece> generatedPieces = new List<PuzzlePiece>();

        // 그리드 데이터
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

            Debug.Log($"[CastPuzzleGenerator] {piecesCount}개의 조각이 맞물려 {gridSize}x{gridSize}x{gridSize} 큐브를 이루는 퍼즐 생성 완료!");
        }

        private void GenerateVoxelGrid()
        {
            puzzleGrid = new int[gridSize, gridSize, gridSize];
            
            // 배열 초기화 (-1은 빈 공간, 즉 아직 깎이지 않은 덩어리)
            for (int x = 0; x < gridSize; x++)
                for (int y = 0; y < gridSize; y++)
                    for (int z = 0; z < gridSize; z++)
                        puzzleGrid[x, y, z] = -1;

            float totalVolume = Mathf.Pow(gridSize, 3);
            int targetCellsPerPiece = Mathf.CeilToInt(totalVolume / piecesCount);
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
                    for (int x = 0; x < gridSize; x++)
                        for (int y = 0; y < gridSize; y++)
                            for (int z = 0; z < gridSize; z++)
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

                            // 방향 무작위 섞기 (편향된 확장 방지)
                            for (int i = 0; i < directions.Length; i++)
                            {
                                Vector3Int temp = directions[i];
                                int rnd = Random.Range(i, directions.Length);
                                directions[i] = directions[rnd];
                                directions[rnd] = temp;
                            }

                            foreach (Vector3Int neighDir in directions)
                            {
                                Vector3Int neighbor = curr + neighDir;
                                if (neighbor.x >= 0 && neighbor.x < gridSize && neighbor.y >= 0 && neighbor.y < gridSize && neighbor.z >= 0 && neighbor.z < gridSize)
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
                            // 사방이 막혀서 더 못 자라면 프론티어에서 탈락
                            if (!expanded) activeFrontier.RemoveAt(randIdx);
                        }

                        // 완성된 조각 확정 이전에, 남은 -1 공간이 두 동강 나지 않았는지(Connected) 검사
                        if (AreRemainingCellsConnected(-1))
                        {
                            foreach (Vector3Int cell in pieceCells)
                                puzzleGrid[cell.x, cell.y, cell.z] = p;

                            carved = true;
                            break;
                        }
                        else
                        {
                            // 쪼개졌다면 이번 조각 깎기는 무효화 (되돌리기)
                            foreach (Vector3Int cell in pieceCells)
                                puzzleGrid[cell.x, cell.y, cell.z] = -1;
                        }
                    }
                }
                
                if (!carved) Debug.LogWarning($"Piece {p} 조각 깎기 실패.");
            }

            // [추가] 고립된 남은 -1 조각들 병합 방지 (안전장치)
            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    for (int z = 0; z < gridSize; z++)
                    {
                        if (puzzleGrid[x, y, z] == -1)
                        {
                            puzzleGrid[x, y, z] = 0;
                        }
                    }
                }
            }
        }

        // 특정 ID의 블록들이 모두 하나로 연결되어 있는지(연결요소가 1개인지) 너비우선탐색(BFS)으로 검사
        private bool AreRemainingCellsConnected(int targetValue)
        {
            Vector3Int startNode = new Vector3Int(-1, -1, -1);
            int targetCount = 0;

            for (int x = 0; x < gridSize; x++)
                for (int y = 0; y < gridSize; y++)
                    for (int z = 0; z < gridSize; z++)
                        if (puzzleGrid[x, y, z] == targetValue)
                        {
                            targetCount++;
                            if (startNode.x == -1) startNode = new Vector3Int(x, y, z);
                        }

            // 공간이 아예 없거나 1칸이면 무조건 연결된 것임
            if (targetCount <= 1) return true;

            int connectedCount = 0;
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            bool[,,] visited = new bool[gridSize, gridSize, gridSize];
            Vector3Int[] dirs = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };

            queue.Enqueue(startNode);
            visited[startNode.x, startNode.y, startNode.z] = true;
            connectedCount++;

            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();
                foreach (Vector3Int dir in dirs)
                {
                    Vector3Int n = curr + dir;
                    if (n.x >= 0 && n.x < gridSize && n.y >= 0 && n.y < gridSize && n.z >= 0 && n.z < gridSize)
                    {
                        if (!visited[n.x, n.y, n.z] && puzzleGrid[n.x, n.y, n.z] == targetValue)
                        {
                            visited[n.x, n.y, n.z] = true;
                            queue.Enqueue(n);
                            connectedCount++;
                        }
                    }
                }
            }

            // 시작점에서 갈 수 있는 덩어리의 개수가 전체 개수와 같다면 하나로 이어진 것
            return connectedCount == targetCount;
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
            Vector3 centerOffset = new Vector3((gridSize - 1) / 2f, (gridSize - 1) / 2f, (gridSize - 1) / 2f);
            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    for (int z = 0; z < gridSize; z++)
                    {
                        int pieceId = puzzleGrid[x, y, z];
                        if (pieceId >= 0 && pieceId < piecesCount)
                        {
                            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            cube.transform.SetParent(pieceParents[pieceId].transform);

                            // 큐브의 로컬 위치 지정 (중앙 정렬)
                            cube.transform.localPosition = new Vector3(x, y, z) - centerOffset;
                            cube.transform.localScale = Vector3.one;
                        }
                    }
                }
            }
        }

        private bool IsExposed(Vector3Int cell, Vector3Int dir, int solidValue)
        {
            Vector3Int curr = cell + dir;
            // 지정된 방향(dir)으로 쭉 나아갔을 때 장애물(solidValue)이 있는지 검사
            while (curr.x >= 0 && curr.x < gridSize && curr.y >= 0 && curr.y < gridSize && curr.z >= 0 && curr.z < gridSize)
            {
                if (puzzleGrid[curr.x, curr.y, curr.z] == solidValue) return false;
                curr += dir;
            }
            return true;
        }


    }
}
