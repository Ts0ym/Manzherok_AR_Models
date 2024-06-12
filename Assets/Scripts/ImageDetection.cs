using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ImageTracking : MonoBehaviour
{
    public GameObject[] firstModelsPack;
    public GameObject[] secondModelsPack;
    public GameObject[] thirdModelsPack;
    public AudioPlayer audioPlayer;
    
    private ObjectPool[] firstModelsPool;
    private ObjectPool[] secondModelsPool;
    private ObjectPool[] thirdModelsPool;
    
    private ARTrackedImageManager trackedImageManager => GetComponent<ARTrackedImageManager>();
    private List<PositionData> positionData;
    private Vector3 _markPosition;
    private List<GameObject> instantiatedModels = new List<GameObject>();
    private int chosenPack = 0;
    private float movementTimeout = 15f;
    private float timeSinceLastMovement = 0.0f;
    private const int AccelerometerSamples = 5;
    private Queue<Vector3> _accelerometerReadings = new Queue<Vector3>();
    private readonly float _movementThreshold = 0.04f;
    private bool isSleeping;
    private Dictionary<TrackableId, TrackingState> trackedImageStates = new Dictionary<TrackableId, TrackingState>();

    [SerializeField] private Camera arCamera;
    [SerializeField] private GameObject _sleepModeScreen;
    [SerializeField] private GameObject _buttonsGroup;
    [SerializeField] private ARSession _arSession;

    private void Awake()
    {
        Screen.SetResolution(1200, 540, false);
    }

    private void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.targetFrameRate = 20;
        
        _arSession.gameObject.SetActive(true);
        _arSession.matchFrameRateRequested = false;
        _arSession.attemptUpdate = false;
        QualitySettings.vSyncCount = 0;

        firstModelsPool = CreatePools(firstModelsPack);
        secondModelsPool = CreatePools(secondModelsPack);
        thirdModelsPool = CreatePools(thirdModelsPack);

        trackedImageManager.requestedMaxNumberOfMovingImages = 1;

        if (PlayerPrefs.HasKey("DeviceNumber"))
        {
            int deviceNumber = PlayerPrefs.GetInt("DeviceNumber");
            Debug.Log($"DeviceNumber found: {deviceNumber}");
            OnChangeModelsClick(deviceNumber);
        }

        StartCoroutine(StartTrackingWithDelay(3f)); // Запускаем отслеживание изображения с периодичностью
        StartCoroutine(CheckDeviceMovement());
        PeriodicResourceCleanup();
    }
    
    private void OnEnable()
    {
        trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
    }

    private void OnDisable()
    {
        trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        StopAllCoroutines();
        FreeResources();
    }

    
    private IEnumerator CheckDeviceMovement()
    {
        while (true)
        {
            MovingHandler();
            yield return new WaitForSeconds(0.5f); 
        }
    }
    
    void PeriodicResourceCleanup()
    {
        StartCoroutine(ResourceCleanupRoutine());
    }
    
    IEnumerator ResourceCleanupRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(240f);
            FreeResources();
        }
    }

    private ObjectPool[] CreatePools(GameObject[] modelPack)
    {
        ObjectPool[] pools = new ObjectPool[modelPack.Length];
        for (int i = 0; i < modelPack.Length; i++)
        {
            pools[i] = new ObjectPool(modelPack[i]);
        }
        return pools;
    }
    
    bool IsSleepTime()
    {
        int currentUtcTime = DateTime.Now.ToLocalTime().Hour;
        // return currentUtcTime <= 9 || currentUtcTime >= 22;
        return false;
    }

    void MovingHandler()
    {
        if(!IsSleepTime() && !isSleeping) return;
        
        Vector3 currentAcceleration = Input.acceleration;
        _accelerometerReadings.Enqueue(currentAcceleration);

        while (_accelerometerReadings.Count > AccelerometerSamples)
        {
            _accelerometerReadings.Dequeue();
        }

        if (IsDeviceMoving())
        {
            timeSinceLastMovement = 0.0f;
            if (isSleeping)
            {
                SetSleepMode(false);
                RestartARSession();
            }
        }
        else
        {
            timeSinceLastMovement += Time.deltaTime;
            if (timeSinceLastMovement > movementTimeout)
            {
                if (!isSleeping && IsSleepTime())
                {
                    SetSleepMode(true);
                    _arSession.attemptUpdate = false;
                }
            }
        }
    }

    private bool IsDeviceMoving()
    {
        if (_accelerometerReadings.Count < AccelerometerSamples)
        {
            // Недостаточно данных для определения движения
            return false;
        }

        Vector3 averageAcceleration = Vector3.zero;
        foreach (Vector3 acceleration in _accelerometerReadings)
        {
            averageAcceleration += acceleration;
        }
        averageAcceleration /= _accelerometerReadings.Count;

        Vector3 currentAcceleration = Input.acceleration;
        Vector3 delta = currentAcceleration - averageAcceleration;

        // Проверка, превышает ли любое из значений отклонение порог движения
        bool isMoving = Mathf.Abs(delta.x) > _movementThreshold || Mathf.Abs(delta.y) > _movementThreshold || Mathf.Abs(delta.z) > _movementThreshold;
        return isMoving;
    }
    IEnumerator StartTrackingWithDelay(float delay)
    {
        while (true)
        {
            if (trackedImageManager.enabled == false)
            {
                yield return new WaitForSeconds(delay);
                trackedImageManager.enabled = true;
            }
            else
            {
                yield return null;
            }
        }
    }
    
    private void SetSleepMode(bool enableSleeping)
    {
        Debug.Log($"SetSleepMode called with enableSleeping: {enableSleeping}");
        if (isSleeping == enableSleeping)
        {
            return;
        }
        
        isSleeping = enableSleeping;
        _sleepModeScreen.gameObject.SetActive(enableSleeping);
        _arSession.enabled = !enableSleeping;
        arCamera.enabled = !enableSleeping;
        trackedImageManager.enabled = !enableSleeping;

        foreach (var model in instantiatedModels)
        {
            model.SetActive(!enableSleeping);
        }

        if (!enableSleeping)
        {
            RestartARSession();
        }
    }

    private void RestartARSession()
    {
        try
        {
            Debug.Log("RestartARSession called.");
            if (_arSession.enabled)
            {
                _arSession.Reset();
            }
            FreeResources();
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        try
        {
            foreach (var trackedImage in eventArgs.added)
            {
                if (trackedImage.trackingState == TrackingState.Tracking)
                {
                    audioPlayer.PlaySound();
                    HandleTrackedImage(trackedImage);
                    UpdateTrackingState(trackedImage.trackableId, trackedImage.trackingState);
                }
            }

            foreach (var trackedImage in eventArgs.updated)
            {
                if (trackedImage.trackingState == TrackingState.Tracking)
                {
                    HandleTrackedImage(trackedImage);
                    UpdateTrackingState(trackedImage.trackableId, trackedImage.trackingState);
                    audioPlayer.PlaySound();
                    trackedImageManager.enabled = false;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void UpdateTrackingState(TrackableId trackableId, TrackingState state)
    {
        trackedImageStates[trackableId] = state;
    }

    private void HandleTrackedImage(ARTrackedImage trackedImage)
    {
        try
        {
            if (instantiatedModels.Count == 0)
            {
                audioPlayer.PlaySound();
                InstantiateModels(chosenPack, trackedImage.transform);
            }
            PlaceModels();
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void PlaceModels()
    {
        for (int i = 0; i < instantiatedModels.Count; i++)
        {
            try
            {
                PositionData currentData = positionData[i];

                Vector3 localPosition = ClampPosition(currentData.position);
                Quaternion localRotation = Quaternion.Euler(currentData.rotation);
                Vector3 localScale = ClampScale(new Vector3(currentData.scale, currentData.scale, currentData.scale));
                instantiatedModels[i].transform.localPosition = localPosition;
                instantiatedModels[i].transform.localRotation = localRotation;
                instantiatedModels[i].transform.localScale = localScale;

            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }
    
    private Vector3 ClampPosition(Vector3 position)
    {
        float maxPositionValue = 10000f;
        return new Vector3(
            Mathf.Clamp(position.x, -maxPositionValue, maxPositionValue),
            Mathf.Clamp(position.y, -maxPositionValue, maxPositionValue),
            Mathf.Clamp(position.z, -maxPositionValue, maxPositionValue)
        );
    }

    private Vector3 ClampScale(Vector3 scale)
    {
        float maxScaleValue = 1000f;
        float minScaleValue = 0.001f;
        return new Vector3(
            Mathf.Clamp(scale.x, minScaleValue, maxScaleValue),
            Mathf.Clamp(scale.y, minScaleValue, maxScaleValue),
            Mathf.Clamp(scale.z, minScaleValue, maxScaleValue)
        );
    }

    private void InstantiateModels(int packNumber, Transform markerTransform)
    {
        try
        {
            ObjectPool[] chosenModelsPool;
            switch (packNumber)
            {
                case 1:
                    chosenModelsPool = firstModelsPool;
                    break;
                case 2:
                    chosenModelsPool = secondModelsPool;
                    break;
                case 3:
                    chosenModelsPool = thirdModelsPool;
                    break;
                default:
                    chosenModelsPool = firstModelsPool;
                    break;
            }
        
            ClearInstantiatedModels();
        
            foreach (var t in chosenModelsPool)
            {
                GameObject instantiatedModel = t.Get();
                instantiatedModel.transform.SetParent(markerTransform, false);
                instantiatedModel.transform.localPosition = Vector3.zero;
                instantiatedModel.transform.localScale = Vector3.one;
                instantiatedModel.SetActive(true);
                instantiatedModels.Add(instantiatedModel);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void ClearInstantiatedModels()
    {
        foreach (var model in instantiatedModels)
        {
            model.SetActive(false);
            if (firstModelsPack.Contains(model))
            {
                firstModelsPool[Array.IndexOf(firstModelsPack, model)].ReturnToPool(model);
            }
            else if (secondModelsPack.Contains(model))
            {
                secondModelsPool[Array.IndexOf(secondModelsPack, model)].ReturnToPool(model);
            }
            else if (thirdModelsPack.Contains(model))
            {
                thirdModelsPool[Array.IndexOf(thirdModelsPack, model)].ReturnToPool(model);
            }
        }
        instantiatedModels.Clear();
    }

    public void OnReloadBtnClick()
    {
        try
        {
            _arSession.Reset();
            PlayerPrefs.DeleteKey("DeviceNumber");
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.name);
            FreeResources();
            
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    public void OnChangeModelsClick(int chosenModelsNumber)
    {
        try
        {
            _buttonsGroup.SetActive(false);
            PlayerPrefs.SetInt("DeviceNumber", chosenModelsNumber);
            string jsonUrl;
            switch (chosenModelsNumber)
            {
                case (1):
                    jsonUrl = "https://service.awake.su/manzherok/ar/config_1.json";
                    break;
                case(2):
                    jsonUrl = "https://service.awake.su/manzherok/ar/config_2.json";
                    break;
                case(3):
                    jsonUrl = "https://service.awake.su/manzherok/ar/config_3.json";
                    break;
                default:
                    jsonUrl = "https://service.awake.su/manzherok/ar/config_1.json";
                    break;
            }

            StartCoroutine(ConfigLoader.LoadFile((value) =>
            {
                positionData = value;
                chosenPack = chosenModelsNumber;
                trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
                FreeResources();
            }
            , jsonUrl));
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }
    
    private void FreeResources()
    {
        GC.Collect();
        Resources.UnloadUnusedAssets();
    }
}


