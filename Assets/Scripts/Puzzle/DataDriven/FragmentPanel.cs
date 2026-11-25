using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnotherOne.Puzzle.DataDriven
{
    /// <summary>
    /// Панель фрагментов кода, полностью управляемая данными.
    /// Центральный цикл: клик по плитке -> цифра добавляется в состояние ->
    /// при совпадении с кодом панель открывается (смена цвета и надписи).
    ///
    /// Данные: StreamingAssets/puzzle_config.json (или файл из configFileName).
    /// Новый элемент добавляется дописыванием объекта в массив fragments —
    /// без изменения и перекомпиляции кода.
    /// </summary>
    [DisallowMultipleComponent]
    public class FragmentPanel : MonoBehaviour
    {
        [Header("Данные")]
        public string configFileName = "puzzle_config.json";
        public string extendedConfigFileName = "puzzle_config.extended.json";

        [Header("Внешний вид")]
        public float tileSize = 0.34f;
        public float spacing = 0.1f;
        public Color baseColor = new Color(0.92f, 0.92f, 0.92f);
        public Color plateColor = new Color(0.16f, 0.38f, 0.60f);
        public Color collectedColor = new Color(0.20f, 0.75f, 0.35f);
        public Color errorColor = new Color(0.85f, 0.25f, 0.25f);

        [Header("Размещение")]
        public bool autoPlaceInFrontOfPlayer = true;
        public float autoPlaceDistance = 3.2f;

        [Header("Отладка")]
        public bool logState = true;

        private PanelConfig _config;
        private string _configFileNameInUse = "puzzle_config.json";
        private readonly List<FragmentTile> _tiles = new List<FragmentTile>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private string _entered = "";
        private bool _solved;
        private Transform _plate;
        private TextMesh _statusText;

        public PanelConfig CurrentConfig => _config;
        public int FragmentCount => _config != null && _config.fragments != null ? _config.fragments.Length : 0;

        private void Start()
        {
            if (autoPlaceInFrontOfPlayer) PlaceInFrontOfPlayer();
            Build(LoadConfig(configFileName));
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                string next = _configFileNameInUse == configFileName ? extendedConfigFileName : configFileName;
                Debug.Log("[FragmentPanel] Переключение набора данных: " + next);
                Build(LoadConfig(next));
                return;
            }

            if (_solved) return;
        }

        // ---------- загрузка и валидация данных ----------

        public PanelConfig LoadConfig(string fileName)
        {
            _configFileNameInUse = fileName;
            string path = Path.Combine(Application.streamingAssetsPath, fileName);

            if (!File.Exists(path))
            {
                Debug.LogWarning("[FragmentPanel] Файл данных не найден: " + path + " — беру значения по умолчанию.");
                return DefaultConfig();
            }

            PanelConfig cfg;
            try
            {
                cfg = JsonUtility.FromJson<PanelConfig>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[FragmentPanel] Данные повреждены (" + e.Message + "). Беру значения по умолчанию.");
                return DefaultConfig();
            }

            if (cfg == null)
            {
                Debug.LogError("[FragmentPanel] Пустой конфиг. Беру значения по умолчанию.");
                return DefaultConfig();
            }

            return Sanitize(cfg);
        }

        /// <summary>
        /// Контролируемая реакция на ошибочные и граничные данные:
        /// битые записи отбрасываются по одной, приложение не падает.
        /// </summary>
        private PanelConfig Sanitize(PanelConfig cfg)
        {
            var valid = new List<FragmentEntry>();
            if (cfg.fragments != null)
            {
                var usedIds = new HashSet<string>();
                foreach (var f in cfg.fragments)
                {
                    if (f == null) { Warn("пустая запись фрагмента"); continue; }
                    if (string.IsNullOrEmpty(f.id)) { Warn("у фрагмента нет id"); continue; }
                    if (!usedIds.Add(f.id)) { Warn("дублирующийся id: " + f.id); continue; }
                    if (string.IsNullOrEmpty(f.digit) || f.digit.Length != 1 || !char.IsDigit(f.digit[0]))
                    {
                        Warn("некорректная цифра у фрагмента " + f.id + ": '" + f.digit + "'");
                        continue;
                    }
                    valid.Add(f);
                }
            }

            cfg.fragments = valid.ToArray();

            if (string.IsNullOrEmpty(cfg.code))
            {
                Warn("код не задан — собираю из цифр фрагментов по порядку");
                var sb = new System.Text.StringBuilder();
                foreach (var f in cfg.fragments) sb.Append(f.digit);
                cfg.code = sb.ToString();
            }

            if (cfg.fragments.Length == 0)
            {
                Warn("не осталось валидных фрагментов — беру значения по умолчанию");
                return DefaultConfig();
            }

            if (cfg.columns < 1) cfg.columns = 1;
            if (cfg.columns > 8) cfg.columns = 8;

            return cfg;
        }

        private static void Warn(string message) =>
            Debug.LogWarning("[FragmentPanel] Данные: " + message);

        private static PanelConfig DefaultConfig()
        {
            return new PanelConfig
            {
                title = "ПАНЕЛЬ ФРАГМЕНТОВ",
                code = "137",
                columns = 3,
                fragments = new[]
                {
                    new FragmentEntry { id = "f1", digit = "1" },
                    new FragmentEntry { id = "f2", digit = "3" },
                    new FragmentEntry { id = "f3", digit = "7" }
                }
            };
        }

        // ---------- построение панели ----------

        private void PlaceInFrontOfPlayer()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;
            origin.y = Mathf.Max(0.9f, origin.y - 0.15f);

            float dist = autoPlaceDistance;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, autoPlaceDistance + 2f) && hit.distance > 1.6f)
                dist = Mathf.Max(1.6f, hit.distance - 0.25f);

            transform.position = origin + dir * dist;
            // forward панели смотрит НА игрока, поэтому лицевая сторона — +Z
            transform.rotation = Quaternion.LookRotation(-dir);
        }

        private void Build(PanelConfig cfg)
        {
            _config = cfg;
            _entered = "";
            _solved = false;

            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
            _tiles.Clear();

            float step = tileSize + spacing;
            int cols = Mathf.Max(1, cfg.columns);
            int rows = Mathf.CeilToInt(cfg.fragments.Length / (float)cols);
            float width = cols * step;
            float height = rows * step;

            _plate = MakeBox("Plate", new Vector3(0f, 0f, -0.04f),
                new Vector3(width + 0.16f, height + 0.16f, 0.05f), plateColor);

            for (int i = 0; i < cfg.fragments.Length; i++)
            {
                var f = cfg.fragments[i];
                int col = i % cols;
                int row = i / cols;
                float px = -width * 0.5f + step * 0.5f + col * step;
                float py = height * 0.5f - step * 0.5f - row * step;

                Color tileColor = baseColor;
                if (!string.IsNullOrEmpty(f.color) && ColorUtility.TryParseHtmlString(f.color, out Color parsed))
                    tileColor = parsed;

                var box = MakeBox("Tile_" + f.id, new Vector3(px, py, 0f),
                    new Vector3(tileSize, tileSize, 0.05f), tileColor);

                var label = MakeText("Digit_" + f.id, transform, f.digit, 0.045f, new Vector3(px, py, 0.05f));

                var tile = box.gameObject.AddComponent<FragmentTile>();
                tile.Setup(this, f, label);
                _tiles.Add(tile);
            }

            var title = MakeText("Title", transform, cfg.title, 0.028f, new Vector3(0f, height * 0.5f + 0.26f, 0.03f));
            title.anchor = TextAnchor.MiddleCenter;
            title.color = Color.white;

            _statusText = MakeText("Status", transform, "ВВЕДЕНО: _", 0.026f, new Vector3(0f, -height * 0.5f - 0.26f, 0.03f));
            _statusText.anchor = TextAnchor.MiddleCenter;
            _statusText.color = Color.white;

            if (logState)
                Debug.Log("[FragmentPanel] Загружено: " + _configFileNameInUse +
                          " | фрагментов: " + cfg.fragments.Length + " | код: " + cfg.code);
        }

        private Transform MakeBox(string name, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            var rend = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            rend.sharedMaterial = mat;

            _spawned.Add(go);
            return go.transform;
        }

        private TextMesh MakeText(string name, Transform parent, string text, float size, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.black;

            _spawned.Add(go);
            return tm;
        }

        // ---------- логика центрального цикла ----------

        public void OnTileClicked(FragmentTile tile)
        {
            if (_solved || _config == null) return;

            _entered += tile.Digit;
            if (logState) Debug.Log("[FragmentPanel] Введено: " + _entered);

            if (_config.code.StartsWith(_entered))
            {
                if (_entered == _config.code) Solve();
                else Refresh("ВВЕДЕНО: " + _entered);
            }
            else
            {
                _entered = "";
                Refresh("НЕВЕРНО, СБРОС");
                tile.Flash(errorColor);
                if (logState) Debug.Log("[FragmentPanel] Неверный ввод — состояние сброшено");
            }
        }

        private void Solve()
        {
            _solved = true;
            Refresh("ДОСТУП РАЗРЕШЁН");
            if (_plate != null)
            {
                var rend = _plate.GetComponent<Renderer>();
                if (rend != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.color = collectedColor;
                    rend.sharedMaterial = mat;
                }
            }
            if (logState) Debug.Log("[FragmentPanel] Код собран: " + _config.code);
        }

        private void Refresh(string status)
        {
            if (_statusText != null) _statusText.text = status;
        }
    }
}
