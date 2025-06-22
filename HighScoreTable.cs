using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class HighScoreTable : MonoBehaviour
{
    public Font font;  // Optional, for TMP styling
    public Vector2 cellSize = new Vector2(160, 40);
    public Vector2 startPosition = new Vector2(0, 0);
    public float spacing = 2f;

    private GameObject table;

    void Start()
    {
        AwsGameStatePersistor gp = this.GetComponent<AwsGameStatePersistor>();
        if(gp != null)
        {
            gp.FetchState(onSuccess: data =>
            {
                this.RenderTable(data);
            });
        }
    }

    void LateUpdate()
    {
        transform.LookAt(Camera.main.transform);
        transform.Rotate(0, 180f, 0); // Face camera, flip if needed
    }


    public void RenderTable(Dictionary<StateItemId, string> data)
    {
        if (table != null)
        {
            Destroy(table);
        }

        table = new GameObject("TableGrid");
        GameObject tableParent = table;
        tableParent.transform.SetParent(this.transform);
        
        RectTransform parentRect = tableParent.AddComponent<RectTransform>();
        parentRect.anchorMin = new Vector2(0.5f, 1f);
        parentRect.anchorMax = new Vector2(0.5f, 0.5f);
        parentRect.pivot = new Vector2(0.5f, 1f);
        parentRect.anchoredPosition = startPosition;
        parentRect.localPosition = new Vector3(startPosition.x, startPosition.y, 0f); // Z = 0 to ensure visibility


        VerticalLayoutGroup layoutGroup = tableParent.AddComponent<VerticalLayoutGroup>();
        layoutGroup.spacing = spacing;
        layoutGroup.childControlHeight = true;
        layoutGroup.childControlWidth = false;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = false;

        ContentSizeFitter fitter = tableParent.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var entry in data)
        {
            GameObject row = new GameObject("Row");
            row.transform.SetParent(tableParent.transform);
            row.transform.localPosition = new Vector3(0, 0, 0);

            HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = spacing;
            rowLayout.childControlHeight = false;
            rowLayout.childControlWidth = false;

            AddCell(row.transform, entry.Key.key);   // Player Name
            AddCell(row.transform, entry.Value); // Score
        }

    }

    void AddCell(Transform parent, string text)
    {
        GameObject cell = new GameObject("Cell");
        cell.transform.SetParent(parent);
        cell.transform.localPosition = new Vector3(0, 0, 0);

        RectTransform rect = cell.AddComponent<RectTransform>();
        rect.sizeDelta = cellSize;

        // Add Image for cell background/border
        Image img = cell.AddComponent<Image>();
        img.color = new Color(0.1f, 0.1f, 0.1f, 0.5f); // semi-transparent dark background

        // Add TMP text
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(cell.transform);
        textGO.transform.localPosition = new Vector3(0, 0, 0);

        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    
}
