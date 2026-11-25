using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnotherOne.Puzzle
{
    /// <summary>
    /// Расставляет существующие листки с кодом (CodeSheet / Paper_With_Code_N) по данным из JSON.
    /// Новый листок = новая запись в файле данных: код игры не меняется.
    /// Привязка цифр к листкам уже реализована в SecretCodeManager (autoBindSheets по имени).
    /// </summary>
    [DisallowMultipleComponent]
    public class CodeSheetsSpawner : MonoBehaviour
    {
        [Header("Данные")]
        public string configFileName = "sheets_config.json";
        public string extendedConfigFileName = "sheets_config.extended.json";

        [Header("Реестр префабов (имя из JSON -> ассет)")]
        public SheetPrefab[] prefabs = new SheetPrefab[0];

        [Header("Отладка")]
        public bool logState = true;
        public bool clearBeforeSpawn = true;
        [Tooltip("Листки — физические объекты; при спавне фиксируем их, чтобы они оставались там, где задано данными")]
        public bool freezePhysics = true;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _map = new Dictionary<string, GameObject>();
        private string _currentFile;

        /// <summary>Сколько листков заспавнено сейчас.</summary>
        public int SpawnedCount => _spawned.Count;

        private void Start()
        {
            BuildMap();
            Rebuild(configFileName);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F2)) return;
            string next = _currentFile == configFileName ? extendedConfigFileName : configFileName;
            Debug.Log("[CodeSheets] Переключение набора данных: " + next);
            Rebuild(next);
        }

        private void BuildMap()
        {
            _map.Clear();
            foreach (var p in prefabs)
            {
                if (p == null || p.prefab == null || string.IsNullOrEmpty(p.name)) continue;
                _map[p.name] = p.prefab;
            }
        }

        // ---------- данные ----------

        public SheetsConfig LoadConfig(string fileName)
        {
            _currentFile = fileName;
            string path = Path.Combine(Application.streamingAssetsPath, fileName);

            if (!File.Exists(path))
            {
                Debug.LogWarning("[CodeSheets] Файл данных не найден: " + path + " — листки не создаются.");
                return new SheetsConfig();
            }

            SheetsConfig cfg;
            try
            {
                cfg = JsonUtility.FromJson<SheetsConfig>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[CodeSheets] Данные повреждены (" + e.Message + ") — листки не создаются.");
                return new SheetsConfig();
            }

            return cfg == null ? new SheetsConfig() : Sanitize(cfg);
        }

        /// <summary>Битые записи пропускаются по одной, остальное строится; игра не падает.</summary>
        private SheetsConfig Sanitize(SheetsConfig cfg)
        {
            var ok = new List<SheetEntry>();
            var used = new HashSet<string>();

            if (cfg.sheets != null)
            {
                foreach (var s in cfg.sheets)
                {
                    if (s == null) { Warn("пустая запись"); continue; }
                    if (string.IsNullOrEmpty(s.prefab)) { Warn("не указан prefab"); continue; }
                    if (!_map.ContainsKey(s.prefab)) { Warn("префаб не найден в реестре: " + s.prefab); continue; }
                    if (!used.Add(s.prefab)) { Warn("дубликат листка: " + s.prefab); continue; }
                    if (float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.z)) { Warn("некорректные координаты у " + s.prefab); continue; }
                    ok.Add(s);
                }
            }

            cfg.sheets = ok.ToArray();
            return cfg;
        }

        private static void Warn(string m) => Debug.LogWarning("[CodeSheets] Данные: " + m);

        // ---------- построение ----------

        public void Rebuild(string fileName)
        {
            SheetsConfig cfg = LoadConfig(fileName);

            if (clearBeforeSpawn)
            {
                foreach (var go in _spawned) if (go != null) Destroy(go);
                _spawned.Clear();
            }

            foreach (var s in cfg.sheets)
            {
                GameObject prefab = _map[s.prefab];
                GameObject inst = Instantiate(prefab, transform);
                inst.name = prefab.name; // имя без "(Clone)" — по нему SecretCodeManager находит листок
                inst.transform.localPosition = new Vector3(s.x, s.y, s.z);
                // rotX — наклон листа, rotY — разворот, rotZ — прокрутка в плоскости экрана (применяется последней).
                inst.transform.localRotation =
                    Quaternion.Euler(0f, 0f, s.rotZ) *
                    Quaternion.Euler(0f, s.rotY, 0f) *
                    Quaternion.Euler(s.rotX, 0f, 0f);

                if (freezePhysics)
                {
                    foreach (var rb in inst.GetComponentsInChildren<Rigidbody>())
                    {
                        rb.useGravity = false;
                        rb.isKinematic = true;
                    }
                }

                _spawned.Add(inst);
            }

            // цифры подставляются существующим менеджером кода
            if (SecretCodeManager.HasInstance)
                SecretCodeManager.Instance.ApplyToAllSheets();

            if (logState)
                Debug.Log("[CodeSheets] " + fileName + " | листков: " + _spawned.Count +
                          " | код: " + (SecretCodeManager.HasInstance ? SecretCodeManager.Instance.Code : "—"));
        }
    }
}
