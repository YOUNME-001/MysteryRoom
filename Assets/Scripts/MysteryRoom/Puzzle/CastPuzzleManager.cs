using UnityEngine;
using System;

namespace MysteryRoom.Puzzle
{
    /// <summary>
    /// 퍼즐이 해결되었는지 런타임에 확인하고 이벤트를 방출하는 매니저 클래스입니다.
    /// 프리팹을 구울 때 제너레이터(CastPuzzleGenerator)는 삭제되고 이 클래스만 남아 역할을 수행합니다.
    /// </summary>
    public class CastPuzzleManager : MonoBehaviour
    {
        public Action OnPuzzleCompleted; // 퍼즐이 완전히 풀렸을 때 호출되는 액션

        private int totalPieces;
        private int solvedPiecesCount = 0;

        void Start()
        {
            // 런타임 시작 시점에 자신이 가진 모든 자식 퍼즐 조각의 총 개수를 계산합니다
            PuzzlePiece[] pieces = GetComponentsInChildren<PuzzlePiece>(true);
            totalPieces = pieces.Length;
        }

        public void ReportPieceSolved()
        {
            solvedPiecesCount++;
            
            Debug.Log($"[CastPuzzleManager] Pieces Total: {totalPieces}");
            Debug.Log($"[CastPuzzleManager] Solved Pieces: {solvedPiecesCount}");
            
            if (solvedPiecesCount >= totalPieces && totalPieces > 0)
            {
                Debug.Log("[CastPuzzleManager] All puzzle pieces solved! Puzzle Completed!");
                OnPuzzleCompleted?.Invoke();
            }
        }
    }
}
