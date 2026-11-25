using System;

namespace AnotherOne.Puzzle.DataDriven
{
    /// <summary>
    /// Модель данных панели фрагментов. Читается из StreamingAssets/puzzle_config*.json.
    /// Любое новое поле/элемент добавляется в JSON — код не меняется.
    /// </summary>
    [Serializable]
    public class FragmentEntry
    {
        public string id;
        public string digit;
        public float x;
        public float y;
        public float z;
        public string color;
    }

    [Serializable]
    public class PanelConfig
    {
        public string title = "ПАНЕЛЬ ФРАГМЕНТОВ";
        public string code = "";
        public int columns = 3;
        public FragmentEntry[] fragments = new FragmentEntry[0];
    }
}
