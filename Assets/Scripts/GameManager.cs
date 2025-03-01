using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro; // Make sure to include this at the top of your script


[System.Serializable]
public class GameParameters
{
    public float energyDepletionRate;
    public float energyRecoveryPerTargetFruit;
    public float rotationSpeed = 10f;
}

public class GameManager : MonoBehaviour
{
    public static GameManager instance { get; private set; }
    private static Dictionary<string, float[]> embeddings;
    public static List<string> collectedWords = new List<string>();

    private GameParameters gameParameters;

    public Light directionalLight;
    public float rotationSpeed = 10f;
    private float currentRotation = 0f;

    //Scoring system
    public string targetWord = "good";
    public string avoidWord = "bad";
    public float wordSelectionThreshold = 0.5f; // Percentage of the dictionary to select words from

    public long maxPointsPerWord = 1000;
    public static long score = 0;

    public float maxEnergy = 100f;
    public float currentEnergy;
    public float energyDepletionRate = 1f;
    public float energyRecoveryPerTargetFruit = 20f;

    private bool gameOverCalled = false;

    public string mainMenuScene = "Menu";

    public bool embeddingsLoaded = false;

    public Button playButton;



    public GameObject gameOverUI = null;
    public GameObject canvasCentral;
    public GameObject canvasHelp;

    public void AToggleHelp()
    {
        if (canvasCentral != null && canvasHelp != null)
        {
            // Toggle the active state of both canvases
            canvasCentral.SetActive(!canvasCentral.activeSelf);
            canvasHelp.SetActive(!canvasHelp.activeSelf);
        }
    }


    private IEnumerator LoadGameParameters()
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, "GameParameters.json");
            string url = filePath;

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string json = www.downloadHandler.text;
                    gameParameters = JsonUtility.FromJson<GameParameters>(json);
                    ApplyGameParameters();
                }
                else
                {
                    Debug.LogError("Failed to load game parameters: " + www.error);
                }
            }
        }
    }

    private void ApplyGameParameters()
    {
        if (gameParameters != null)
        {
            energyDepletionRate = gameParameters.energyDepletionRate;
            energyRecoveryPerTargetFruit = gameParameters.energyRecoveryPerTargetFruit;
            rotationSpeed = gameParameters.rotationSpeed;
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            if (gameOverUI != null)
            {
                DontDestroyOnLoad(gameOverUI);
                gameOverUI.SetActive(false); // Hide the game over UI initially
            }
            DontDestroyOnLoad(gameObject);


            StartCoroutine(LoadEmbeddings());
            SceneManager.sceneLoaded += OnSceneLoaded;
            currentEnergy = maxEnergy;
            //Debug.Log($"Initial Energy: {currentEnergy}");
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                StartCoroutine(LoadGameParameters());
            }


        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    Debug.Log(scene.name);


}


    private void EnablePlayButton()
        {
            if (playButton != null)
            {
                playButton.interactable = true;
                Debug.Log("Play button enabled.");
            }
        }

    private IEnumerator LoadEmbeddings()
    {
        embeddings = new Dictionary<string, float[]>();

        string[] fileNames = {"glove.6B.50d.95MB.txt", "glove.6B.50d.txt", "improved_supercompact.txt" };

        foreach (string fileName in fileNames)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, fileName);

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // WebGL build
                string url = filePath;

                using (UnityWebRequest www = UnityWebRequest.Get(url))
                {
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        ProcessEmbeddings(www.downloadHandler.text);
                        Debug.Log($"Embeddings file '{fileName}' loaded successfully.");
                        embeddingsLoaded = true;
                        EnablePlayButton();
                        yield break;
                    }
                }
            }
            else
            {
                // Local build
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    ProcessEmbeddings(string.Join("\n", lines));
                    Debug.Log($"Embeddings file '{fileName}' loaded successfully.");
                    embeddingsLoaded = true;
                    EnablePlayButton();
                    yield break;
                }
            }
        }

        Debug.LogError("No embeddings file found.");
    }

    private void ProcessEmbeddings(string embeddingsText)
    {
        string[] lines = embeddingsText.Split('\n');

        foreach (string line in lines)
        {
            string[] parts = line.Split(' ');
            string word = parts[0];
            float[] embedding = new float[parts.Length - 1];

            for (int i = 1; i < parts.Length; i++)
            {
                embedding[i - 1] = float.Parse(parts[i]);
            }

            embeddings[word] = embedding;
        }

        Debug.Log("GloVe embeddings loaded successfully.");
    }

    public static float[] GetEmbedding(string word)
    {
        if (embeddings.ContainsKey(word))
        {
            return embeddings[word];
        }
        return null;
    }

    

    private Light FindDirectionalLight()
    {
        Light[] lights = FindObjectsOfType<Light>();
        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }
        return null;
    }

    public Quaternion GetDirectionalLightRotation()
{
    if (directionalLight != null)
    {
        return directionalLight.transform.rotation;
    }
    return Quaternion.identity;
}


    private void GameOver()
    {
        // Assuming gameOverUI is the root of your UI hierarchy, e.g., CanvasGameOver
        if (gameOverUI!= null)
        {
            // Activate the Game Over UI
            gameOverUI.SetActive(true);
        }

        // Disable player movement
        MovementComponent movementComponent = FindObjectOfType<MovementComponent>();
        if (movementComponent!= null)
        {
            movementComponent.enabled = false;
        }

        // Calculate the average embedding and find the closest word
        float[] averageEmbedding = CalculateAverageEmbedding();
        if (averageEmbedding!= null)
        {
            string finalEssence = FindClosestWord(averageEmbedding);
            if (!string.IsNullOrEmpty(finalEssence))
            {
                // Use the full path to find the FinalEssenceText GameObject
                Transform finalEssenceTextTransform = gameOverUI.transform.Find("/CanvasGameOver/GameOverMenu/Essence/Wood/FinalEssenceText");
                if (finalEssenceTextTransform!= null)
                {
                    TextMeshProUGUI finalEssenceText = finalEssenceTextTransform.GetComponent<TextMeshProUGUI>();

                    if (finalEssenceText!= null)
                    {
                        finalEssenceText.text = $"{finalEssence}";
                    }
                    else
                    {
                        Debug.LogError("FinalEssenceText GameObject does not have a TextMeshProUGUI component.");
                    }
                }
                else
                {
                    Debug.LogError("Could not find FinalEssenceText GameObject.");
                }
            }
        }
    }

    

    private void Start()
    {
        SelectRandomWords();
        // Set the initial state of the canvases
        if (canvasCentral != null && canvasHelp != null)
        {
            canvasCentral.SetActive(true);
            canvasHelp.SetActive(false);
        }
        else
        {
            Debug.LogError("CanvasCentral or CanvasHelp not assigned in the Inspector.");
        }
    }

    private void SelectRandomWords()
    {
        if (embeddings != null && embeddings.Count > 0)
          {
            List<string> keys = new List<string>(embeddings.Keys);
            int thresholdCount = Mathf.FloorToInt(keys.Count * wordSelectionThreshold);

            int randomTargetIndex = Random.Range(0, thresholdCount);
            targetWord = keys[randomTargetIndex];

            // Ensure the Avoid word is different from the Target word
            string avoidWordCandidate;
            do
            {
                int randomAvoidIndex = Random.Range(0, thresholdCount);
                avoidWordCandidate = keys[randomAvoidIndex];
            } while (avoidWordCandidate == targetWord);

            avoidWord = avoidWordCandidate;
        }
    }

    public void RestartGame()
    {
        // Reset variables and state
        currentEnergy = maxEnergy;
        score = 0;
        collectedWords.Clear();
        gameOverCalled = false;
        if (gameOverUI != null)
        {
            gameOverUI.SetActive(false); // Hide the game over UI
        }

        // Select new random words for the next game
        SelectRandomWords();

        // Reload the current scene to restart the game
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private float[] CalculateAverageEmbedding()
{
    if (collectedWords.Count == 0) return null;

    float[] sumEmbedding = new float[embeddings[collectedWords[0]].Length];
    foreach (string word in collectedWords)
    {
        float[] wordEmbedding = embeddings[word];
        for (int i = 0; i < sumEmbedding.Length; i++)
        {
            sumEmbedding[i] += wordEmbedding[i];
        }
    }

    for (int i = 0; i < sumEmbedding.Length; i++)
    {
        sumEmbedding[i] /= collectedWords.Count;
    }

    return sumEmbedding;
}

private string FindClosestWord(float[] averageEmbedding)
{
    float maxSimilarity = float.NegativeInfinity;
    string closestWord = null;

    foreach (var pair in embeddings)
    {
        float similarity = CosineSimilarity(averageEmbedding, pair.Value);
        if (similarity > maxSimilarity)
        {
            maxSimilarity = similarity;
            closestWord = pair.Key;
        }
    }

    return closestWord;
}

    private void Update()
    {
        // ...
        if (directionalLight == null)
        {
            directionalLight = FindDirectionalLight();
        }

        if (directionalLight != null)
        {
            currentRotation += rotationSpeed * Time.deltaTime;
            if (currentRotation >= 360f)
            {
                currentRotation -= 360f;
            }
            directionalLight.transform.rotation = Quaternion.Euler(currentRotation, -30f, 0f);
        }

        if (SceneManager.GetActiveScene().name != mainMenuScene)
        {
        // Update energy and check for game over only in the game scene
        currentEnergy -= energyDepletionRate * Time.deltaTime;
        currentEnergy = Mathf.Clamp(currentEnergy, 0f, maxEnergy);
        //Debug.Log($"Current Energy: {currentEnergy}");

        if (currentEnergy <= 0f && !gameOverCalled)
            {
                GameOver();
                gameOverCalled = true;
            }
        }   
    }

    public static string[] GetAllWords()
    {
        if (embeddings != null)
        {
            return embeddings.Keys.ToArray();
        }
        return null;
    }

    public static long CalculatePoints(string word)
    {
        float targetSimilarity = CosineSimilarity(GetEmbedding(word), GetEmbedding(instance.targetWord));
        float avoidSimilarity = CosineSimilarity(GetEmbedding(word), GetEmbedding(instance.avoidWord));

        // Determine which word is closer based on similarity
        bool isCloserToTarget = targetSimilarity > avoidSimilarity;

        // Calculate the score based on the winning similarity
        float winningSimilarity = isCloserToTarget? targetSimilarity : avoidSimilarity;
        long score = (long)(winningSimilarity * instance.maxPointsPerWord);

        // Adjust the score based on whether it's closer to the target or avoid word
        if (isCloserToTarget)
        {
            // If closer to the target, ensure the score is positive
            score = (long)Mathf.Abs(score);
        }
        else
        {
            // If closer to the avoid word, make the score negative
            score = -(long)Mathf.Abs(score);
        }

        // Ensure the score is within a reasonable range

        return score;
    }

    public static void AddCollectedWord(string word)
    {
        if (!string.IsNullOrEmpty(word) && !collectedWords.Contains(word))
        {
            collectedWords.Add(word);
            long points = CalculatePoints(word);
            score += points;

            if (points > 0)
            {
                instance.currentEnergy += instance.energyRecoveryPerTargetFruit;
                instance.currentEnergy = Mathf.Clamp(instance.currentEnergy, 0f, instance.maxEnergy);
                //Debug.Log($"Energy after collecting target fruit: {instance.currentEnergy}");
                
            }

            //Debug.Log($"Collected word: {word}, Points: {points}, Total Score: {score}, Energy: {instance.currentEnergy}");
        }
    }

    public static string GetCollectedWordsText()
    {
        return string.Join("\n", collectedWords);
    }

    

    public static float CosineSimilarity(float[] vector1, float[] vector2)
    {
        float dotProduct = 0.0f;
        float magnitude1 = 0.0f;
        float magnitude2 = 0.0f;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Mathf.Sqrt(magnitude1);
        magnitude2 = Mathf.Sqrt(magnitude2);

        if (magnitude1 != 0.0f && magnitude2 != 0.0f)
        {
            return dotProduct / (magnitude1 * magnitude2);
        }
        else
        {
            return 0.0f;
        }
    }

    public static string[] GetSimilarWords(string word, int count = 5, float threshold = 0.1f)
    {
        if (embeddings.ContainsKey(word))
        {
            float[] wordVector = embeddings[word];
            var similarWords = embeddings
                .Where(pair => pair.Key != word)
                .Select(pair => new { Word = pair.Key, Similarity = CosineSimilarity(wordVector, pair.Value) })
                .Where(item => item.Similarity >= threshold)
                .OrderByDescending(item => item.Similarity)
                .Take(count)
                .Select(item => item.Word)
                .ToArray();

            return similarWords;
        }

        return new string[0];
    }
}