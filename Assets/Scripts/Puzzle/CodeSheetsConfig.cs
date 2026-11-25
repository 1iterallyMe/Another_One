using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnotherOne.Puzzle
{
    /// <summary>Одна запись данных: какой существующий листок и где поставить.</summary>
    [Serializable]
    public class SheetEntry
    {
        public string prefab;
        public float x;
        public float y;
        public float z;
        public float rotX;
        public float rotY = 180f;
        public float rotZ;
    }

    /// <summary>Формат файла данных (StreamingAssets/sheets_config*.json).</summary>
    [Serializable]
    public class SheetsConfig
    {
        public string title = "";
        public SheetEntry[] sheets = new SheetEntry[0];
    }

    /// <summary>Реестр префабов: имя из данных -> ассет.</summary>
    [Serializable]
    public class SheetPrefab
    {
        public string name;
        public GameObject prefab;
    }
}
