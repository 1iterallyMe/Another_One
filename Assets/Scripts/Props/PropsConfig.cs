using System;
using UnityEngine;

namespace AnotherOne.Props
{
    /// <summary>Одна запись данных: какой существующий префаб и где поставить.</summary>
    [Serializable]
    public class PropEntry
    {
        public string prefab;
        public float x;
        public float y;
        public float z;
        public float rotX;
        public float rotY;
        public float rotZ;
    }

    /// <summary>Формат файла данных (StreamingAssets/props_config*.json).</summary>
    [Serializable]
    public class PropsConfig
    {
        public string title = "";
        public PropEntry[] props = new PropEntry[0];
    }

    /// <summary>Реестр префабов: имя из данных -> ассет.</summary>
    [Serializable]
    public class PropPrefab
    {
        public string name;
        public GameObject prefab;
    }
}
