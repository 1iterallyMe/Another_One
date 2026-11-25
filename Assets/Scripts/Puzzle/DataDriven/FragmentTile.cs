using System.Collections;
using UnityEngine;

namespace AnotherOne.Puzzle.DataDriven
{
    /// <summary>Кликабельная плитка с цифрой. Создаётся из данных (FragmentEntry).</summary>
    public class FragmentTile : MonoBehaviour
    {
        private FragmentPanel _panel;
        private FragmentEntry _entry;
        private TextMesh _label;
        private Renderer _rend;
        private Color _baseColor = Color.white;

        public string Digit => _entry != null ? _entry.digit : "";

        public void Setup(FragmentPanel panel, FragmentEntry entry, TextMesh label)
        {
            _panel = panel;
            _entry = entry;
            _label = label;
            _rend = GetComponent<Renderer>();
            if (_rend != null && _rend.sharedMaterial != null) _baseColor = _rend.sharedMaterial.color;
        }

        private void OnMouseDown()
        {
            if (_panel != null) _panel.OnTileClicked(this);
        }

        public void Flash(Color color)
        {
            if (_rend != null)
            {
                var mat = new Material(_rend.sharedMaterial);
                mat.color = color;
                _rend.sharedMaterial = mat;
            }
            StartCoroutine(RestoreAfter(0.35f));
        }

        private IEnumerator RestoreAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_rend != null)
            {
                var mat = new Material(_rend.sharedMaterial);
                mat.color = _baseColor;
                _rend.sharedMaterial = mat;
            }
        }
    }
}
