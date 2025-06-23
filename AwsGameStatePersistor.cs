using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;


public class StateItemId
{
    public string computerId;
    public string key;

    public StateItemId(string computerId, string key)
    {
        this.computerId = computerId;
        this.key = key;
    }
    
    public override bool Equals(object obj)
    {
        return Equals(obj as StateItemId);
    }

    public bool Equals(StateItemId other)
    {
        return other != null &&
               computerId == other.computerId &&
               key == other.key;
    }

    public override int GetHashCode()
    {
        // Safe hash combination
        return HashCode.Combine(computerId, key);
    }
}

[System.Serializable]
public class StringWrapper
{
    public string value;
}


public class AwsGameStatePersistor : MonoBehaviour
{
    
    public string GameItemKey = "YOUR_GAME_STATE_ID";
    public string region = "us-east-1";
    public string tableName = "games_state";
    public Boolean SortNumericValues = true;
    public int MaxItemsToFetch = int.MaxValue;
    public Boolean UniqueItemsPerComputer = false;

    private string accessKey = "your aws access key goes here";
    private string secretKey = "your aws secret key goes here";
    private string computerId = "";


    /// <summary>
    /// 
    /// </summary>
    void Start()
    {
        if (this.UniqueItemsPerComputer)
        {
            this.computerId = PlayerPrefs.GetString("computer_id", null);
            if (this.computerId == null || this.computerId.Length == 0)
            {
                this.computerId = this.GenerateComputerId();
                PlayerPrefs.SetString("computer_id", this.computerId);
                PlayerPrefs.Save();
            }

            Debug.Log("Computer Id = " + this.computerId);
        } else
        {
            Debug.Log("Unique items per computer is not needed");
        }
 
    }

    private string GenerateComputerId()
    {
        long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return EncodeBase62(ms);
    }

    public void SaveState(Dictionary<StateItemId, string> state)
    {
        
        var simpleState = ConvertToSimpleDictionary(state);
        var stateStr = SerializeSimple(simpleState);
        Debug.Log("Save state to cloud storage:\n" + stateStr);
        StartCoroutine(SendToDynamoDB(stateStr));
    }

    public void FetchState(Action<Dictionary<StateItemId, string>> onSuccess)
    {
        StartCoroutine(ReadFromDynamoDB($"{GameItemKey}",
        onSuccess: data =>
        {
            data = ExtractDataFieldWithRegex(data);
            var State = DeserializeSimple(data);
            if (this.SortNumericValues)
            {
                State = StripAndSortByValue(State);
            } 
            
            Debug.Log("Received item: " + data);
            Debug.Log("this.State length = " + State.Count);
            onSuccess?.Invoke(State);
                        
        },
        onError: error =>
        {
            Debug.LogError("Error reading item: " + error);
        }));
    }

    public static string ExtractDataFieldWithRegex(string json)
    {
        var match = Regex.Match(json, @"""Data"":\s*\{\s*""S"":\s*""(.*?)""\s*\}");
        if (match.Success)
        {
            return Regex.Unescape(match.Groups[1].Value);
        }
        return null;
    }

    IEnumerator SendToDynamoDB(string jsonPayload)
    {
        string service = "dynamodb";
        string host = $"dynamodb.{region}.amazonaws.com";
        string endpoint = $"https://{host}/";

        string amzDate = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");

        string requestPayload = GeneratePutItemPayload(jsonPayload);
        string payloadHash = Hash(requestPayload);

        string canonicalUri = "/";
        string canonicalQueryString = "";
        string canonicalHeaders = $"content-type:application/x-amz-json-1.0\nhost:{host}\nx-amz-date:{amzDate}\n";
        string signedHeaders = "content-type;host;x-amz-date";
        string canonicalRequest = $"POST\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        string algorithm = "AWS4-HMAC-SHA256";
        string credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        string stringToSign = $"{algorithm}\n{amzDate}\n{credentialScope}\n{Hash(canonicalRequest)}";

        byte[] signingKey = GetSignatureKey(secretKey, dateStamp, region, service);
        string signature = ToHex(HmacSHA256(stringToSign, signingKey));

        string authorizationHeader = $"{algorithm} Credential={accessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/x-amz-json-1.0");
        request.SetRequestHeader("X-Amz-Date", amzDate);
        request.SetRequestHeader("Authorization", authorizationHeader);
        request.SetRequestHeader("X-Amz-Target", "DynamoDB_20120810.PutItem");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Data sent successfully: " + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"Error sending data: {request.error}\n{request.downloadHandler.text}");
        }
    }

    public IEnumerator ReadFromDynamoDB(string primaryKey, Action<string> onSuccess, Action<string> onError)
    {
        string service = "dynamodb";
        string host = $"dynamodb.{region}.amazonaws.com";
        string endpoint = $"https://{host}/";

        string amzDate = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");

        string requestPayload = $@"
{{
    ""TableName"": ""{tableName}"",
    ""Key"": {{
        ""game_id"": {{""S"": ""{primaryKey}""}}
    }}
}}";

        string payloadHash = Hash(requestPayload);

        string canonicalUri = "/";
        string canonicalQueryString = "";
        string canonicalHeaders = $"content-type:application/x-amz-json-1.0\nhost:{host}\nx-amz-date:{amzDate}\n";
        string signedHeaders = "content-type;host;x-amz-date";
        string canonicalRequest = $"POST\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        string algorithm = "AWS4-HMAC-SHA256";
        string credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        string stringToSign = $"{algorithm}\n{amzDate}\n{credentialScope}\n{Hash(canonicalRequest)}";

        byte[] signingKey = GetSignatureKey(secretKey, dateStamp, region, service);
        string signature = ToHex(HmacSHA256(stringToSign, signingKey));

        string authorizationHeader = $"{algorithm} Credential={accessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/x-amz-json-1.0");
        request.SetRequestHeader("X-Amz-Date", amzDate);
        request.SetRequestHeader("Authorization", authorizationHeader);
        request.SetRequestHeader("X-Amz-Target", "DynamoDB_20120810.GetItem");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Data retrieved successfully: " + request.downloadHandler.text);
            onSuccess?.Invoke(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"Error retrieving data: {request.error}\n{request.downloadHandler.text}");
            onError?.Invoke(request.downloadHandler.text);
        }
    }

    string GeneratePutItemPayload(string data)
    {
        // Use Unity's JsonUtility to escape the string safely
        string wrapped = JsonUtility.ToJson(new StringWrapper { value = data });

        // The result is {"value":"escaped_string"} — so we just extract the string part
        int colonIndex = wrapped.IndexOf(':');
        string jsonEscapedData = wrapped.Substring(colonIndex + 1).TrimEnd('}');

        return $@"
{{
    ""TableName"": ""{tableName}"",
    ""Item"": {{
        ""game_id"": {{""S"": ""{this.GameItemKey}""}},
        ""Data"": {{""S"": {jsonEscapedData} }}
    }}
}}";
    }


    string Hash(string data)
    {
        using (var sha256 = SHA256.Create())
        {
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return ToHex(bytes);
        }
    }

    byte[] HmacSHA256(string data, byte[] key)
    {
        using (var kha = new HMACSHA256(key))
        {
            return kha.ComputeHash(Encoding.UTF8.GetBytes(data));
        }
    }

    byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        byte[] kDate = HmacSHA256(dateStamp, Encoding.UTF8.GetBytes("AWS4" + key));
        byte[] kRegion = HmacSHA256(regionName, kDate);
        byte[] kService = HmacSHA256(serviceName, kRegion);
        byte[] kSigning = HmacSHA256("aws4_request", kService);
        return kSigning;
    }

    string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (byte b in bytes)
            sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }


    public Dictionary<StateItemId, string> DeserializeSimple(string input)
    {
        var dict = new Dictionary<StateItemId, string>();
        var lines = input.Split('\n');
        int counter = 0;

        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ',' }, 2); // Only split at the first comma
            if (parts.Length == 2)
            {

                StateItemId key = null;
                string keyStr = parts[0].Trim();

                if (this.UniqueItemsPerComputer)
                {
                    if (keyStr.Length < 7)
                    {
                        keyStr = "000000" + keyStr;
                    }
                    key = new StateItemId(keyStr.Substring(0, 6), keyStr.Substring(6));
                } else
                {
                    key = new StateItemId("", keyStr);
                }

                dict[key] = parts[1].Trim();
                counter++;
                if(counter > this.MaxItemsToFetch)
                {
                    break;
                }
            }
        }

        return dict;
    }

    public static string SerializeSimple(Dictionary<string, string> dict)
    {
        var lines = new List<string>();
        foreach (var kvp in dict)
        {
            lines.Add($"{kvp.Key},{kvp.Value}");
        }
        return string.Join("\n", lines);
    }

    public static string EncodeBase62(long value)
    {
        const string base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var result = new System.Text.StringBuilder();
        do
        {
            result.Insert(0, base62[(int)(value % 62)]);
            value /= 62;
        } while (value > 0);

        var s = result.ToString();
        if (s.Length > 6) s = s.Substring(0, 6);
        return s.PadLeft(6, '0');
    }

    private Dictionary<StateItemId, string> StripAndSortByValue(Dictionary<StateItemId, string> original)
    {
        return original
        .OrderByDescending(kvp =>
       {
           int number;
           return int.TryParse(kvp.Value, out number) ? number : 0;
       }) // or long.Parse / double.Parse if needed
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private Dictionary<string, string> ConvertToSimpleDictionary(Dictionary<StateItemId, string> original)
    {
        return original
        .Select(kvp => new KeyValuePair<string, string>(
           kvp.Key.computerId + kvp.Key.key,
            kvp.Value
        ))
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public StateItemId CreateStateItem(string key)
    {
        return new StateItemId(this.computerId, key);
    }

}
