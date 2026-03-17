using UnityEngine;

namespace MysteryRoom.Puzzle {

    public class CastPuzzleSolveObserver : MonoBehaviour {

        private void Start(){
            CastPuzzleGenerator.Instance.OnPuzzleCompleted += () => {
                Debug.Log("OnPuzzleCompleted");
            };
        }

    }

}