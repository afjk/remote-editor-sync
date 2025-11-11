using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuntimeHierarchy
{
    /// <summary>
    /// SampleScene起動時にSecondSceneをAdditiveで開く
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        [SerializeField]
        private string sceneToLoad = "SecondScene";

        private void Start()
        {
            LoadSecondScene();
        }

        private void LoadSecondScene()
        {
            // SecondSceneが既にロードされているかチェック
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneToLoad)
                {
                    Debug.Log("[SceneLoader] " + sceneToLoad + " is already loaded.");
                    return;
                }
            }

            // SecondSceneをAdditiveモードでロード
            Debug.Log("[SceneLoader] Loading " + sceneToLoad + " in Additive mode...");
            SceneManager.LoadScene(sceneToLoad, LoadSceneMode.Additive);
        }
    }
}
