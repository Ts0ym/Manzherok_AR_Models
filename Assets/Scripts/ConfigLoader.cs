using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class JsonConfig
{
    public string value;
}

public class PositionData
{
    public Vector3 position;
    public Vector3 rotation;
    public float scale;
}

public class ConfigLoader : MonoBehaviour
{
    public static IEnumerator LoadFile(Action<List<PositionData>> onLoad, string fileURL)
    {
        using var webRequest = UnityWebRequest.Get(fileURL);
        yield return webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError("Ошибка при загрузке: " + webRequest.error);

            if (PlayerPrefs.HasKey("Positions"))
            {
                string jsonData = PlayerPrefs.GetString("Positions");
                Debug.Log("Загрузка из PlayerPrefs: " + jsonData);
                try
                {
                    List<PositionData> positions = parseJsonList<PositionData>(jsonData);
                    onLoad(positions);
                }
                catch (Exception e)
                {
                    Debug.LogError("Ошибка при парсинге JSON из PlayerPrefs: " + e.Message);
                }
            }
            else
            {
                Debug.LogError("Нет сохраненных данных в PlayerPrefs");
            }
        }
        else if (webRequest.result == UnityWebRequest.Result.Success)
        {
            try
            {
                string jsonData = webRequest.downloadHandler.text;
                Debug.Log("Загрузка из URL: " + fileURL);
                Debug.Log("Полученные данные: " + jsonData);
                PlayerPrefs.SetString("Positions", jsonData);
                PlayerPrefs.Save();
                List<PositionData> positions = parseJsonList<PositionData>(jsonData);
                onLoad(positions);
            }
            catch (Exception e)
            {
                Debug.LogError("Ошибка при парсинге JSON из URL: " + e.Message);
            }
        }
    }

    public static List<T> parseJsonList<T>(string jsonString)
    {
        try
        {
            return JsonConvert.DeserializeObject<List<T>>(jsonString);
        }
        catch (Exception e)
        {
            Debug.LogError("Ошибка при десериализации JSON: " + e.Message);
            return new List<T>();
        }
    }
}
