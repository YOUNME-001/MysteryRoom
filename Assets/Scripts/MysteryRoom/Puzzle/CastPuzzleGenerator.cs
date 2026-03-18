using UnityEngine;
using System;
using System.Collections.Generic;
using Random = UnityEngine.Random;

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
        [Tooltip("완성된 전체 퍼즐이 차지할 실제 유니티 공간 크기 (예: 1 = 가로세로높이 1미터)")]
        public float cubeSize = 1.0f;
        
        [Range(1, 10)]
        [Tooltip("퍼즐 난이도. 높을수록 다른 방향으로 빠져나가지 못하게 심하게 꼬인(Branching) 얽힘 형태를 띱니다.")]
        public int difficulty = 5;

        public enum PieceCountMode { Auto, Custom }
        [Header("Piece Count Settings")]
        public PieceCountMode pieceCountMode = PieceCountMode.Auto;
        
        [Tooltip("Custom 모드일 때 설정하는 조각 개수")]
        public int customPiecesCount = 4;
        
        private int activePiecesCount = 4; // 런타임에 결정되는 실제 조각 개수

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
            
            // 프리팹으로 구을 때 이 객체에 런타임 감지용 매니저가 붙어있게 강제합니다.
            CastPuzzleManager manager = GetComponent<CastPuzzleManager>();
            if (manager == null) gameObject.AddComponent<CastPuzzleManager>();

            // Auto 모드일 경우 총 볼륨과 난이도를 고려하여 조각 수 자동 계산
            if (pieceCountMode == PieceCountMode.Auto)
            {
                int volume = gridSize * gridSize * gridSize;
                // 난이도가 높을수록 조각 당 차지하는 칸 수가 엄청나게 커져서(거대한 덩어리) 우수수 떨어지지 않고 서로 지독하게 얽힙니다.
                // difficulty 1 -> 조각당 3칸 수준으로 잘게 부서지는 쉬운 퍼즐 (8~9조각)
                // difficulty 10 -> 조각당 12칸 크기, 고작 2~3개의 거대한 쇳덩어리가 엉킨 지옥의 퍼즐
                int targetVolumePerPiece = Mathf.Clamp(2 + difficulty, 3, 15);
                activePiecesCount = Mathf.Max(2, volume / targetVolumePerPiece);
                Debug.Log($"[Auto Piece Mode] Grid Size: {gridSize}x{gridSize}, Difficulty: {difficulty} -> Auto Pieces Count: {activePiecesCount}");
            }
            else
            {
                activePiecesCount = customPiecesCount;
            }

            // 1. 3x3x3 그리드를 각 퍼즐 조각 ID로 채우기 (Flood Fill 방식)
            GenerateVoxelGrid();

            // 2. 그리드 데이터 기반으로 실제 3D 오브젝트(퍼즐 조각) 스폰
            SpawnPiecesFromGrid();

            Debug.Log($"[CastPuzzleGenerator] {activePiecesCount}개의 조각이 맞물려 {gridSize}x{gridSize}x{gridSize} 큐브를 이루는 퍼즐 생성 완료!");
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
            int targetCellsPerPiece = Mathf.CeilToInt(totalVolume / activePiecesCount);
            Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };

            // 핵심 퍼즐 알고리즘: 밖에서부터 한 조각씩 깎아서(역순 조립) 무조건 풀릴 수 있는 형태를 보장함
            for (int p = activePiecesCount - 1; p >= 1; p--)
            {
                // 생성 가능한 다중 턴(Multi-turn) 탈출 경로 후보들을 무작위로 생성합니다.
                List<List<MoveSegment>> pathCandidates = new List<List<MoveSegment>>();

                // 0. 3번 꺾이는 경로 (3-turn) - 지옥 난이도 (7 이상) 전용, 가장 먼저 시도!
                if (difficulty >= 7)
                {
                    for (int i = 0; i < difficulty * 5; i++)
                    {
                        Vector3Int d1 = directions[Random.Range(0, 6)];
                        Vector3Int d2 = directions[Random.Range(0, 6)];
                        Vector3Int d3 = directions[Random.Range(0, 6)];
                        Vector3Int d4 = directions[Random.Range(0, 6)];
                        if (d1 == -d2 || d1 == d2 || d2 == -d3 || d2 == d3 || d3 == -d4 || d3 == d4) continue;
                        pathCandidates.Add(new List<MoveSegment>() { new MoveSegment(d1, 1), new MoveSegment(d2, 1), new MoveSegment(d3, 1), new MoveSegment(d4, 99) });
                    }
                }

                // 1. 2번 꺾이는 경로 (2-turn) 등록
                for (int i = 0; i < difficulty * 4; i++)
                {
                    Vector3Int d1 = directions[Random.Range(0, 6)];
                    Vector3Int d2 = directions[Random.Range(0, 6)];
                    Vector3Int d3 = directions[Random.Range(0, 6)];
                    if (d1 == -d2 || d1 == d2 || d2 == -d3 || d2 == d3) continue;
                    pathCandidates.Add(new List<MoveSegment>() { new MoveSegment(d1, 1), new MoveSegment(d2, 1), new MoveSegment(d3, 99) });
                }

                // 2. 1번 꺾이는 경로 (1-turn) 등록
                for (int i = 0; i < difficulty * 3; i++)
                {
                    Vector3Int d1 = directions[Random.Range(0, 6)];
                    Vector3Int d2 = directions[Random.Range(0, 6)];
                    if (d1 == -d2 || d1 == d2) continue;
                    pathCandidates.Add(new List<MoveSegment>() { new MoveSegment(d1, 1), new MoveSegment(d2, 99) });
                }

                // 3. 직선 경로 (0-turn 스탠다드 경로) - 최후의 안전망으로 맨 마지막에 등록
                List<Vector3Int> safeDirs = new List<Vector3Int>(directions);
                for(int i=0; i<safeDirs.Count; i++) { Vector3Int t = safeDirs[i]; int r = Random.Range(i, safeDirs.Count); safeDirs[i]=safeDirs[r]; safeDirs[r]=t; }
                foreach(Vector3Int d in safeDirs) pathCandidates.Add(new List<MoveSegment>() { new MoveSegment(d, 99) });

                bool carved = false;
                foreach (var path in pathCandidates)
                {
                    List<Vector3Int> exposedCells = new List<Vector3Int>();
                    for (int x = 0; x < gridSize; x++)
                        for (int y = 0; y < gridSize; y++)
                            for (int z = 0; z < gridSize; z++)
                                if (puzzleGrid[x, y, z] == -1 && IsExposedPath(new Vector3Int(x, y, z), path, -1))
                                    exposedCells.Add(new Vector3Int(x, y, z));

                    if (exposedCells.Count > 0)
                    {
                        Vector3Int seed = exposedCells[Random.Range(0, exposedCells.Count)];
                        List<Vector3Int> pieceCells = new List<Vector3Int>();
                        List<Vector3Int> activeFrontier = new List<Vector3Int>();
                        
                        pieceCells.Add(seed);
                        activeFrontier.Add(seed);
                        puzzleGrid[seed.x, seed.y, seed.z] = -2; // 현재 깎는 중인 임시 마커

                        Vector3Int lastGrowthDir = Vector3Int.zero;

                        while (activeFrontier.Count > 0 && pieceCells.Count < targetCellsPerPiece + Random.Range(-1, 2))
                        {
                            Vector3Int bestCurr = Vector3Int.zero;
                            Vector3Int bestNeighbor = Vector3Int.zero;
                            int bestScore = int.MinValue;
                            bool foundCandidate = false;
                            
                            List<int> deadEnds = new List<int>();

                            // 프론티어에 남은 모든 칸을 대상으로 가장 난이도를 높일 수 있는 후보 탐색
                            for (int idx = 0; idx < activeFrontier.Count; idx++)
                            {
                                Vector3Int curr = activeFrontier[idx];
                                bool hasAnyCandidate = false;

                                foreach (Vector3Int neighDir in directions)
                                {
                                    Vector3Int neighbor = curr + neighDir;
                                    if (neighbor.x >= 0 && neighbor.x < gridSize && neighbor.y >= 0 && neighbor.y < gridSize && neighbor.z >= 0 && neighbor.z < gridSize)
                                    {
                                        if (puzzleGrid[neighbor.x, neighbor.y, neighbor.z] == -1) // 아직 덩어리 상태
                                        {
                                            if (IsExposedPath(neighbor, path, -1)) // 선택된 다중 턴 경로(path)를 따라 무사히 빠져나가는지 검증!
                                            {
                                                hasAnyCandidate = true;
                                                
                                                int score = Random.Range(0, 10); 
                                                
                                                // 난이도 상승 1: 극단적인 형태 꺾임 (가중치 대폭 증가)
                                                if (lastGrowthDir != Vector3Int.zero && neighDir != lastGrowthDir && neighDir != -lastGrowthDir)
                                                {
                                                    score += difficulty * 500; 
                                                }

                                                // 난이도 상승 2: 강력한 함정 억제 (직선 탈출로가 없는 곳으로만 파고드는 가중치 폭발)
                                                int otherExposedWays = 0;
                                                foreach (Vector3Int otherDir in directions)
                                                {
                                                    // 임시 1방향(직선) 경로 검사로 꼼수 탈출이 쉬운지 평가 (직선 탈출구가 없다면 완전한 함정 코어)
                                                    if (IsExposedPath(neighbor, new List<MoveSegment>(){new MoveSegment(otherDir, 99)}, -1))
                                                        otherExposedWays++;
                                                }
                                                // 노출된 직통 방향이 0개일수록 강력한 함정 블록이므로 최대 6방향 점수 산정
                                                score += (6 - otherExposedWays) * difficulty * 100;

                                                if (score > bestScore)
                                                {
                                                    bestScore = score;
                                                    bestCurr = curr;
                                                    bestNeighbor = neighbor;
                                                    foundCandidate = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                // 이 칸 주변에 자라날 수 있는 유효한 이웃이 하나도 없으면 데드엔드 선언
                                if (!hasAnyCandidate) deadEnds.Add(idx);
                            }

                            if (foundCandidate)
                            {
                                pieceCells.Add(bestNeighbor);
                                activeFrontier.Add(bestNeighbor);
                                puzzleGrid[bestNeighbor.x, bestNeighbor.y, bestNeighbor.z] = -2;
                                lastGrowthDir = bestNeighbor - bestCurr;
                                
                                // 불필요해진 데드엔드는 뒤에서부터 지워줌
                                for (int i = deadEnds.Count - 1; i >= 0; i--)
                                {
                                    activeFrontier.RemoveAt(deadEnds[i]);
                                }
                            }
                            else
                            {
                                // 더 이상 아무데도 못 자라면 루프 종료
                                activeFrontier.Clear(); 
                            }
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
            float actualScale = cubeSize / (float)gridSize; // 전체 퍼즐 크기를 격자 수로 시분할하여 단위 큐브 스케일 도출

            // 각 Piece ID를 담당할 부모 GameObject 생성
            GameObject[] pieceParents = new GameObject[activePiecesCount];
            for (int i = 0; i < activePiecesCount; i++)
            {
                pieceParents[i] = new GameObject($"CastPiece_{i}");
                pieceParents[i].transform.SetParent(this.transform);
                pieceParents[i].transform.localPosition = Vector3.zero;
                pieceParents[i].transform.localScale = Vector3.one * actualScale; // 시분할된 스케일 적용
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
                        if (pieceId >= 0 && pieceId < activePiecesCount)
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

            // 자식(블록)이 하나도 생성되지 않은(깎기 실패한) 더미 부모는 삭제하고, 유효한 조각만 리스트에 넣습니다.
            for (int i = 0; i < activePiecesCount; i++)
            {
                if (pieceParents[i].transform.childCount == 0)
                {
                    Destroy(pieceParents[i]);
                }
                else
                {
                    PuzzlePiece pieceComp = pieceParents[i].AddComponent<PuzzlePiece>();
                    pieceComp.pieceID = i;
                    // 조각이 몸통(전체 큐브 넓이)에서 완전히 빠져나왔다고 판정할 거리(unlockDistance)
                    pieceComp.unlockDistance = cubeSize * 0.8f;
                    pieceComp.puzzleTotalSize = cubeSize; // 조각의 드래그 이동 물리 속도를 전체 크기에 비례하게 맞춤

                    generatedPieces.Add(pieceComp);
                }
            }
        }

        private class MoveSegment
        {
            public Vector3Int dir;
            public int steps;
            public MoveSegment(Vector3Int d, int s) { dir = d; steps = s; }
        }

        private bool IsExposedPath(Vector3Int cell, List<MoveSegment> path, int solidValue)
        {
            Vector3Int curr = cell;
            foreach (var seg in path)
            {
                for (int i = 0; i < seg.steps; i++)
                {
                    curr += seg.dir;
                    if (curr.x < 0 || curr.x >= gridSize || curr.y < 0 || curr.y >= gridSize || curr.z < 0 || curr.z >= gridSize)
                    {
                        return true; // 영역 밖으로 어느 구간이든 빠져나갔으면 완전 탈출 성공
                    }
                    if (puzzleGrid[curr.x, curr.y, curr.z] == solidValue)
                    {
                        return false; // 장애물 발견되면 탈출 실패
                    }
                }
            }
            return true; // 모든 스텝(경로)을 소진했는데도 영역 밖이 아니라면 실패 간주 (단, 마지막 스텝이 99이므로 무조건 밖으로 나감)
        }

    }
}
