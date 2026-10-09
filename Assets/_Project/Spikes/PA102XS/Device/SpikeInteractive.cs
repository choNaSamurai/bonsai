using System;
using System.Collections;
using PocketArbor.Spike.Render;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace PocketArbor.Spike.Device
{
    /// <summary>
    /// PA-102X-S interactive mode for the owner's phone: the sketch tree with a finger-sized debug menu
    /// (tree / pruning / wind / quality / view / stats). Touch via the Input System (EnhancedTouch); the menu is drawn
    /// with IMGUI as plain rectangles + labels with our own hit-testing (no prefabs, no UI sprites).
    /// Not a measurement mode: IMGUI and the stats text allocate.
    /// <see cref="selfTest"/>: scripted run with a virtual touchscreen (menu, tap-cut, growth slider, growth) that logs
    /// "PA102XS_IT step=..." and holds each state a few seconds so the host can take screenshots.
    /// </summary>
    public sealed class SpikeInteractive : MonoBehaviour
    {
        public Camera cam;
        public bool selfTest;

        private static readonly string[] Species = { "momiji", "yamazakura", "kuromatsu" };
        private static readonly string[] SpeciesUa = { "Момідзі", "Ямадзакура", "Куромацу" };
        private static readonly string[] Styles = { "moyogi", "chokkan", "shakan", "bunjin", "fukinagashi" };
        private static readonly string[] StylesUa = { "Мойоґі", "Чоккан", "Шакан", "Бундзін", "Фукін." };
        private static readonly string[] Tabs = { "Дерево", "Обрізка", "Вітер", "Якість", "Вигляд", "Стат." };
        private static readonly int IdBarkLod = Shader.PropertyToID("_SkBarkLodMat");
        private const int SliderT = 1, SliderWind = 2, SliderLod = 3;
        private const float MinT = 0.02f, GrowSeconds = 10f;

        private SketchTreeRenderer _tree;
        private readonly SketchFrame _frame = new SketchFrame();
        private readonly Color _bg = new Color(0.93f, 0.92f, 0.9f, 1f);

        // Tree parameters (default: Momiji, moyogi, seed 3, t = 1, wind on).
        private int _sp, _st, _seed = 3;
        private float _t = 1f;
        // Options.
        private bool _menuOpen = true, _wind = true, _lowBark, _quality, _leaves = true, _stats = true, _prune = true;
        private float _windStrength = 0.5f, _barkLod = 0.4f;
        private int _tab;
        // Jobs.
        private bool _baked, _scrubbing, _growing;
        private float _growStart, _lastScrubT = -1f;
        private int _jobFrames, _lastJobFrames, _cuts, _woodTrisNoBake = -1;
        private bool _cutPrepared, _snapshotRequested;
        private double _prepMs, _cutMs;
        private string _toast = "";
        private float _toastUntil;
        // View.
        private float _zoom = 1f;
        private Vector2 _pan;
        private float _wpp = 1f;
        // Input.
        private bool _down, _onUi, _moved, _gesture, _tapPending;
        private Vector2 _pressGui, _dragGui, _tapGui, _lastPos;
        private float _lastEmptyTap = -10f;
        private int _candidate = -1, _activeSlider = -1, _releasedSlider = -1;
        private float _pinchDist;
        private Vector2 _pinchMid;
        // Layout (from the last OnGUI).
        private Rect _menuBtn, _panel, _sliderTRect, _growBtn, _tab0;
        private float _dp = 2.5f;
        // Stats.
        private readonly float[] _dts = new float[600];
        private readonly float[] _sortBuf = new float[600];
        private int _dtCount, _dtHead;
        private readonly FrameTiming[] _ft = new FrameTiming[1];
        private float _statsAt;
        private string _statsText = "";
        private float _time;
        // Drawing.
        private Texture2D _white;
        private GUIStyle _text, _btnText, _small, _hint;

        private void Start()
        {
            EnhancedTouchSupport.Enable();
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _dp = Mathf.Max(1.5f, (Screen.dpi > 0 ? Screen.dpi : 400f) / 160f);
            _white = Texture2D.whiteTexture;
            _tree = new SketchTreeRenderer(null, HideFlags.None);
            ApplyQuality();
            Rebuild(1f);
            Toast("Будую дерево…", 3f);
            Debug.Log("PA102XS_IT start api=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceVersion + " screen=" + Screen.width + "x" + Screen.height + " dpi=" + Screen.dpi + " selftest=" + selfTest);
            if (selfTest) StartCoroutine(SelfTest());
        }

        private void OnDestroy()
        {
            if (_tree != null) _tree.Dispose();
        }

        // ------------------------------------------------------------------ tree actions

        /// <summary>Main-thread generator (the worker uses the same TreeGenAdapter, so finish its job first).</summary>
        private SketchFrame Gen(float t)
        {
            if (_tree.Busy) _tree.CompleteNow();
            TreeGenAdapter.Generate(Species[_sp], Styles[_st], _seed, t, _frame);
            return _frame;
        }

        private void ShowNoBake(float t)
        {
            SketchTreeRenderer.Stats st = _tree.ShowNoBake(Gen(t));
            _woodTrisNoBake = st.WoodTris;
            _baked = false;
            _cutPrepared = false;
        }

        private void Rebuild(float t)
        {
            _t = t;
            _growing = false;
            _tree.BeginRebuild(Species[_sp], Styles[_st], _seed, t, 60);
            _baked = false;
            _cutPrepared = false;
            _jobFrames = 0;
            _cuts = 0;
        }

        private bool CanCut => _baked && !_tree.Busy && !_growing && !_scrubbing;

        /// <summary>
        /// Leaf → branch mapping for the displayed tree (renderer PrepareCut: main thread, tens of ms on a phone).
        /// Done on finger-down over a branch, so the release frame only swaps index buffers (as in K4).
        /// </summary>
        private void EnsureCutPrepared()
        {
            if (_cutPrepared) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _tree.PrepareCut();
            _prepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _cutPrepared = true;
        }

        private void CutGroup(int g)
        {
            EnsureCutPrepared();
            _cutMs = _tree.Cut(g, 8).CpuMs;
            _cutPrepared = false; // groups shift after the cut; map again before the next one
            _cuts++;
            _jobFrames = 0;
            Toast("Зріз гілки " + (g + 1), 1.5f);
            Debug.Log("PA102XS_IT cut group=" + g + " prepareMs=" + _prepMs.ToString("0.0") + " cutMs=" + _cutMs.ToString("0.00"));
        }

        private void ApplyQuality()
        {
            _tree.LowEndBark = _lowBark;
            Shader.SetGlobalFloat(IdBarkLod, _lowBark ? _barkLod : 0f);
        }

        private void SetMeshPreset(bool quality)
        {
            if (_tree.Busy) _tree.CompleteNow();
            _quality = quality;
            if (quality) _tree.UseQualityPreset();
            else _tree.UseBudgetPreset();
            Rebuild(_t);
        }

        private void Toast(string s, float seconds)
        {
            _toast = s;
            _toastUntil = _time + seconds;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            _dts[_dtHead] = dt * 1000f;
            _dtHead = (_dtHead + 1) % _dts.Length;
            _dtCount = Math.Min(_dtCount + 1, _dts.Length);
            FrameTimingManager.CaptureFrameTimings();

            HandleInput();

            if (_growing)
            {
                float t = Mathf.Clamp01((_time - _growStart) / GrowSeconds);
                _t = Mathf.Max(MinT, t);
                ShowNoBake(_t);
                if (t >= 1f) Rebuild(1f);
            }
            else if (_activeSlider == SliderT && _down)
            {
                if (Mathf.Abs(_t - _lastScrubT) > 0.002f)
                {
                    _t = Mathf.Max(MinT, _t);
                    ShowNoBake(_t);
                    _lastScrubT = _t;
                    _scrubbing = true;
                }
            }
            else if (_tree.Busy)
            {
                _jobFrames++;
                if (_tree.Tick())
                {
                    _baked = !_tree.Busy;
                    _woodTrisNoBake = -1;
                    if (_baked) _lastJobFrames = _jobFrames;
                }
            }

            if (_releasedSlider == SliderT)
            {
                _scrubbing = false;
                _lastScrubT = -1f;
                Rebuild(Mathf.Max(MinT, _t));
            }
            if (_releasedSlider == SliderLod) ApplyQuality();
            _releasedSlider = -1;

            SketchGlobals.SetWind(_wind ? _windStrength : 0f, _time);
            _tree.Leaves.enabled = _leaves;
        }

        private void LateUpdate()
        {
            SketchCamera.Apply(cam);
            cam.backgroundColor = _bg;
            Rect free = TreeRect();
            _wpp = Mathf.Max(SketchConst.FrameW / Mathf.Max(free.width, 1f), SketchConst.FrameH / Mathf.Max(free.height, 1f)) / _zoom;
            cam.orthographicSize = _wpp * Screen.height * 0.5f;
            float ox = free.center.x - Screen.width * 0.5f, oy = -(free.center.y - Screen.height * 0.5f);
            Transform tr = cam.transform;
            tr.position += tr.right * (-ox * _wpp + _pan.x) + tr.up * (-oy * _wpp + _pan.y);
            if (_snapshotRequested)
            {
                _snapshotRequested = false;
                Snapshot();
            }
        }

        private bool Portrait => Screen.height >= Screen.width;
        private float TopInset => Screen.height - Screen.safeArea.yMax;
        private float BottomInset => Screen.safeArea.y;

        /// <summary>Screen area not covered by the open menu (GUI coordinates, top-left origin).</summary>
        private Rect FreeRect()
        {
            if (!_menuOpen) return new Rect(0, TopInset, Screen.width, Screen.height - TopInset - BottomInset);
            Rect p = PanelRect();
            return Portrait ? new Rect(0, TopInset, Screen.width, p.y - TopInset) : new Rect(0, 0, p.x, Screen.height);
        }

        private float StatsHeight => 10f * 15.5f * _dp + 12f * _dp;

        /// <summary>Where the tree is fitted: the free area minus the header row (stats box / menu button).</summary>
        private Rect TreeRect()
        {
            Rect free = FreeRect();
            float head = 8f * _dp * 2f + (_stats ? StatsHeight : 56f * _dp);
            return new Rect(free.x, free.y + head, free.width, Mathf.Max(1f, free.height - head));
        }

        // ------------------------------------------------------------------ input

        private void HandleInput()
        {
            var touches = ETouch.activeTouches;
            int n = touches.Count;
            if (n >= 2)
            {
                Vector2 a = touches[0].screenPosition, b = touches[1].screenPosition;
                Vector2 mid = (a + b) * 0.5f;
                float dist = Vector2.Distance(a, b);
                if (!_gesture)
                {
                    _gesture = true;
                    _candidate = -1;
                    _activeSlider = -1;
                    if (!_down) _onUi = InUi(new Vector2(mid.x, Screen.height - mid.y));
                }
                else if (!_onUi)
                {
                    if (_pinchDist > 1f) _zoom = Mathf.Clamp(_zoom * dist / _pinchDist, 0.6f, 6f);
                    _pan -= (mid - _pinchMid) * _wpp;
                }
                _pinchDist = dist;
                _pinchMid = mid;
                return;
            }
            if (n == 0)
            {
                if (_down) Release(_lastPos);
                _gesture = false;
                return;
            }
            ETouch t = touches[0];
            Vector2 p = t.screenPosition;
            Vector2 gp = new Vector2(p.x, Screen.height - p.y);
            _lastPos = p;
            switch (t.phase)
            {
                case ETouchPhase.Began:
                    _down = true;
                    _moved = false;
                    _pressGui = gp;
                    _dragGui = gp;
                    _onUi = InUi(gp);
                    _candidate = !_onUi && _prune && CanCut && !_gesture ? Pick(p) : -1;
                    if (_candidate >= 0) EnsureCutPrepared();
                    break;
                case ETouchPhase.Moved:
                case ETouchPhase.Stationary:
                    _dragGui = gp;
                    if ((gp - _pressGui).magnitude > 14f * _dp)
                    {
                        _moved = true;
                        _candidate = -1;
                    }
                    break;
                case ETouchPhase.Ended:
                case ETouchPhase.Canceled:
                    _dragGui = gp;
                    Release(p);
                    break;
            }
        }

        /// <summary>Finger up: UI tap, branch cut (no time limit: hold = preview, slide away = cancel) or double tap.</summary>
        private void Release(Vector2 p)
        {
            if (!_down) return;
            _down = false;
            Vector2 gp = new Vector2(p.x, Screen.height - p.y);
            if (_activeSlider >= 0) _releasedSlider = _activeSlider;
            _activeSlider = -1;
            if (!_moved && !_gesture)
            {
                if (_onUi)
                {
                    _tapGui = gp;
                    _tapPending = true;
                }
                else if (_candidate >= 0 && CanCut)
                {
                    CutGroup(_candidate);
                }
                else if (_time - _lastEmptyTap < 0.35f)
                {
                    _zoom = 1f;
                    _pan = Vector2.zero;
                    _lastEmptyTap = -10f;
                }
                else _lastEmptyTap = _time;
            }
            _candidate = -1;
        }

        private bool InUi(Vector2 gp)
        {
            return _menuBtn.Contains(gp) || (_menuOpen && _panel.Contains(gp));
        }

        /// <summary>Branch group under the finger: screen distance to the projected limb segments minus their radius.</summary>
        private int Pick(Vector2 p)
        {
            SketchFrame f = _tree.Current;
            float best = 26f * _dp;
            int bg = -1;
            float pxPerUnit = 1f / Mathf.Max(_wpp, 1e-4f);
            for (int g = 0; g < f.Ng; g++)
            {
                int c0 = (int)f.Gm[g * 8 + 5], cn = (int)f.Gm[g * 8 + 6];
                for (int c = c0; c < c0 + cn && c < SketchConst.MAXCH; c++)
                {
                    int b0 = SketchConst.OBR + c * 5;
                    for (int s = 0; s < 4; s++)
                    {
                        Vector3 a = Screen3(f, b0 + s), b = Screen3(f, b0 + s + 1);
                        float r = f.Dat[(b0 + s) * 4 + 3] * pxPerUnit;
                        float d = SegDist(p, a, b) - r;
                        if (d < best)
                        {
                            best = d;
                            bg = g;
                        }
                    }
                }
            }
            return bg;
        }

        private Vector3 Screen3(SketchFrame f, int i)
        {
            int o = i * 4;
            return cam.WorldToScreenPoint(new Vector3(f.Dat[o], f.Dat[o + 1], -f.Dat[o + 2]));
        }

        private static float SegDist(Vector2 p, Vector3 a3, Vector3 b3)
        {
            Vector2 a = a3, b = b3, ab = b - a;
            float h = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * h - p).magnitude;
        }

        // ------------------------------------------------------------------ UI

        private Rect PanelRect()
        {
            float row = 56f * _dp, pad = 8f * _dp;
            if (Portrait)
            {
                float h = Mathf.Min(Screen.height * 0.55f, row * 6f + pad * 8.5f) + BottomInset;
                return new Rect(0, Screen.height - h, Screen.width, h);
            }
            float w = Mathf.Min(Screen.width * 0.5f, 520f * _dp);
            return new Rect(Screen.width - w, 0, w, Screen.height);
        }

        private void EnsureStyles()
        {
            if (_text != null) return;
            _text = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15f * _dp), alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _text.normal.textColor = new Color(0.12f, 0.12f, 0.12f);
            _btnText = new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontSize = Mathf.RoundToInt(14f * _dp) };
            _small = new GUIStyle(_text) { fontSize = Mathf.RoundToInt(12f * _dp), alignment = TextAnchor.UpperLeft, wordWrap = false };
            _hint = new GUIStyle(_small) { wordWrap = true };
        }

        private void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private bool Button(Rect r, string label, bool selected = false)
        {
            Fill(r, selected ? new Color(0.36f, 0.55f, 0.36f, 0.95f) : new Color(1f, 1f, 1f, 0.92f));
            Fill(new Rect(r.x, r.yMax - 2f * _dp, r.width, 2f * _dp), new Color(0f, 0f, 0f, 0.18f));
            _btnText.normal.textColor = selected ? Color.white : new Color(0.12f, 0.12f, 0.12f);
            GUI.Label(r, label, _btnText);
            if (selfTest) _rects[label] = r;
            if (_tapPending && r.Contains(_tapGui))
            {
                _tapPending = false;
                return true;
            }
            return false;
        }

        private float Slider(int id, Rect r, float v)
        {
            float inset = 16f * _dp, w = Mathf.Max(1f, r.width - 2f * inset);
            if (_down && _onUi && _activeSlider < 0 && r.Contains(_pressGui)) _activeSlider = id;
            if (_activeSlider == id) v = Mathf.Clamp01((_dragGui.x - r.x - inset) / w);
            float cy = r.center.y;
            Fill(new Rect(r.x + inset, cy - 3f * _dp, w, 6f * _dp), new Color(0f, 0f, 0f, 0.25f));
            Fill(new Rect(r.x + inset, cy - 3f * _dp, w * v, 6f * _dp), new Color(0.36f, 0.55f, 0.36f, 1f));
            float kx = r.x + inset + w * v;
            Fill(new Rect(kx - 16f * _dp, cy - 16f * _dp, 32f * _dp, 32f * _dp), _activeSlider == id ? new Color(0.25f, 0.42f, 0.25f) : new Color(0.36f, 0.55f, 0.36f));
            return v;
        }

        private float SliderX(Rect r, float v)
        {
            float inset = 16f * _dp;
            return r.x + inset + (r.width - 2f * inset) * v;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            float pad = 8f * _dp, row = 56f * _dp;
            Rect free = FreeRect();

            if (_candidate >= 0) DrawCandidate();

            // Menu toggle: top-right of the tree area.
            float bw = 120f * _dp;
            _menuBtn = new Rect(free.xMax - bw - pad, free.y + pad, bw, row);
            if (Button(_menuBtn, _menuOpen ? "Сховати" : "Меню", false)) _menuOpen = !_menuOpen;

            if (_stats) DrawStats(new Rect(free.x + pad, free.y + pad, Mathf.Min(free.width - bw - 3f * pad, 300f * _dp), StatsHeight));

            // Toast / bake status at the top of the tree area (usually empty sky).
            string status = _tree != null && _tree.Busy ? "Будую / запікаю світло: " + _jobFrames + " кадр." : "";
            string line = _time < _toastUntil ? _toast : status;
            if (line.Length > 0)
            {
                var r = new Rect(free.x + pad, TreeRect().y, free.width - 2f * pad, 40f * _dp);
                Fill(r, new Color(1f, 1f, 1f, 0.8f));
                GUI.Label(new Rect(r.x + 10f * _dp, r.y, r.width - 20f * _dp, r.height), line, _text);
            }

            if (_menuOpen) DrawPanel(row, pad);
            _tapPending = false;
        }

        private void DrawCandidate()
        {
            SketchFrame f = _tree.Current;
            if (_candidate >= f.Ng) return;
            int c0 = (int)f.Gm[_candidate * 8 + 5], cn = (int)f.Gm[_candidate * 8 + 6];
            float sz = 7f * _dp;
            var col = new Color(1f, 0.45f, 0.1f, 0.95f);
            for (int c = c0; c < c0 + cn && c < SketchConst.MAXCH; c++)
            {
                int b0 = SketchConst.OBR + c * 5;
                for (int s = 0; s < 4; s++)
                {
                    Vector3 a = Screen3(f, b0 + s), b = Screen3(f, b0 + s + 1);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (5f * _dp)));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector3 q = Vector3.Lerp(a, b, k / (float)steps);
                        Fill(new Rect(q.x - sz * 0.5f, Screen.height - q.y - sz * 0.5f, sz, sz), col);
                    }
                }
            }
        }

        private void DrawStats(Rect r)
        {
            if (_time >= _statsAt)
            {
                _statsAt = _time + 0.25f;
                // Average and 1 % low over the last 2 s of frames.
                int n = 0;
                float sum = 0f;
                while (n < _dtCount && sum < 2000f)
                {
                    float v = _dts[(_dtHead - 1 - n + _dts.Length) % _dts.Length];
                    sum += v;
                    _sortBuf[n++] = v;
                }
                Array.Sort(_sortBuf, 0, n);
                float avg = n > 0 ? sum / n : 0f, p99 = n > 0 ? _sortBuf[Mathf.Clamp(Mathf.CeilToInt(n * 0.99f) - 1, 0, n - 1)] : 0f;
                uint got = FrameTimingManager.GetLatestTimings(1, _ft);
                double cpu = got > 0 ? _ft[0].cpuMainThreadFrameTime : -1, rt = got > 0 ? _ft[0].cpuRenderThreadFrameTime : -1;
                double gpu = got > 0 ? _ft[0].gpuFrameTime : -1;
                SketchTreeRenderer.Stats st = _tree.LastJobStats;
                int wood = _woodTrisNoBake >= 0 ? _woodTrisNoBake : st.WoodTris;
                _statsText =
                    "API: " + SystemInfo.graphicsDeviceType + "\n" +
                    "FPS " + (avg > 0 ? (1000f / avg).ToString("0.0") : "-") + " · 1% low " + (p99 > 0 ? (1000f / p99).ToString("0.0") : "-") + " (2 с)\n" +
                    "CPU " + (cpu > 0 ? cpu.ToString("0.0") : "н/д") + "/" + (rt > 0 ? rt.ToString("0.0") : "н/д") + " · GPU " + (gpu > 0 && gpu < 200 ? gpu.ToString("0.0") : "н/д") + " мс\n" +
                    "Деревина " + wood + " тр.\n" +
                    "Горщик " + st.PotTris + " тр. · листя " + _tree.DisplayedLeafCount + "\n" +
                    "Пам'ять дерева " + (_tree.GpuBytes() / 1048576f).ToString("0.0") + " МБ\n" +
                    "Запікання: " + (_tree.Busy ? "іде, " + _jobFrames + " кадр." : _baked ? "готово, " + _lastJobFrames + " кадр." : "без запікання") + "\n" +
                    "Кора " + (_lowBark ? "LowEnd" : "повна") + " · сітка " + (_quality ? "quality" : "budget") + "\n" +
                    SpeciesUa[_sp] + " · " + StylesUa[_st] + " · #" + _seed + " · " + Mathf.RoundToInt(_t * 100) + "%\n" +
                    "Зрізів: " + _cuts + (_cuts > 0 ? " (підгот. " + _prepMs.ToString("0") + " мс, зріз " + _cutMs.ToString("0.0") + " мс)" : "");
            }
            Fill(r, new Color(1f, 1f, 1f, 0.72f));
            GUI.Label(new Rect(r.x + 8f * _dp, r.y + 6f * _dp, r.width - 16f * _dp, r.height - 12f * _dp), _statsText, _small);
        }

        private void DrawPanel(float row, float pad)
        {
            _panel = PanelRect();
            Fill(_panel, new Color(0.96f, 0.95f, 0.93f, 0.97f));
            float x0 = _panel.x + pad, w = _panel.width - 2f * pad, y = _panel.y + pad;
            if (!Portrait) y += TopInset;
            float tw = w / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tr = new Rect(x0 + i * tw + 2f, y, tw - 4f, row);
                if (i == 0) _tab0 = tr;
                if (Button(tr, Tabs[i], _tab == i)) _tab = i;
            }
            y += row + pad * 1.5f;
            float lw = w * 0.24f, cx = x0 + lw, cw = w - lw, half = w * 0.5f - 4f;

            switch (_tab)
            {
                case 0: // Tree
                {
                    GUI.Label(new Rect(x0, y, lw, row), "Порода", _text);
                    for (int i = 0; i < 3; i++)
                        if (Button(new Rect(cx + i * cw / 3f + 2f, y, cw / 3f - 4f, row), SpeciesUa[i], _sp == i) && _sp != i) { _sp = i; Rebuild(_t); }
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, lw, row), "Стиль", _text);
                    for (int i = 0; i < 5; i++)
                        if (Button(new Rect(cx + i * cw / 5f + 2f, y, cw / 5f - 4f, row), StylesUa[i], _st == i) && _st != i) { _st = i; Rebuild(_t); }
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, lw, row), "Насіння", _text);
                    float bw = cw / 4f;
                    if (Button(new Rect(cx + 2f, y, bw - 4f, row), "-1")) { _seed = Math.Max(0, _seed - 1); Rebuild(_t); }
                    GUI.Label(new Rect(cx + bw, y, bw, row), _seed.ToString(), _btnText);
                    if (Button(new Rect(cx + 2f * bw + 2f, y, bw - 4f, row), "+1")) { _seed++; Rebuild(_t); }
                    if (Button(new Rect(cx + 3f * bw + 2f, y, bw - 4f, row), "Випадк.")) { _seed = UnityEngine.Random.Range(0, 10000); Rebuild(_t); }
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, lw, row), "Ріст " + Mathf.RoundToInt(_t * 100) + "%", _text);
                    _sliderTRect = new Rect(cx, y, cw, row);
                    float nt = Slider(SliderT, _sliderTRect, _t);
                    if (!_growing) _t = nt;
                    y += row + pad;
                    _growBtn = new Rect(x0, y, half, row);
                    if (Button(_growBtn, _growing ? "Росте…" : "Ріст 0-100% за 10 с", _growing) && !_growing)
                    {
                        _growing = true;
                        _growStart = _time;
                        _baked = false;
                    }
                    if (Button(new Rect(x0 + w * 0.5f + 4f, y, half, row), "Відновити дерево")) Rebuild(_t);
                    break;
                }
                case 1: // Pruning
                {
                    GUI.Label(new Rect(x0, y, w, row * 1.6f), "Торкніться гілки й тримайте — вона підсвітиться. Відпустіть — зріз. Відведіть палець — скасування.", _text);
                    y += row * 1.6f + pad;
                    if (Button(new Rect(x0, y, half, row), _prune ? "Обрізка: увімк." : "Обрізка: вимк.", _prune)) _prune = !_prune;
                    if (Button(new Rect(x0 + w * 0.5f + 4f, y, half, row), "Відновити дерево")) Rebuild(_t);
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, w, row), "Зрізів: " + _cuts + (CanCut ? "" : " (зачекайте запікання)"), _text);
                    break;
                }
                case 2: // Wind
                {
                    if (Button(new Rect(x0, y, w, row), _wind ? "Вітер: увімк." : "Вітер: вимк.", _wind)) _wind = !_wind;
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, lw, row), "Сила " + Mathf.RoundToInt(_windStrength * 100) + "%", _text);
                    _windStrength = Slider(SliderWind, new Rect(cx, y, cw, row), _windStrength);
                    break;
                }
                case 3: // Quality
                {
                    GUI.Label(new Rect(x0, y, lw, row), "Кора", _text);
                    if (Button(new Rect(cx + 2f, y, cw * 0.5f - 4f, row), "Повна", !_lowBark) && _lowBark) { _lowBark = false; ApplyQuality(); }
                    if (Button(new Rect(cx + cw * 0.5f + 2f, y, cw * 0.5f - 4f, row), "LowEnd", _lowBark) && !_lowBark) { _lowBark = true; ApplyQuality(); }
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, lw, row), "LOD " + _barkLod.ToString("0.00"), _text);
                    _barkLod = Slider(SliderLod, new Rect(cx, y, cw, row), _barkLod);
                    if (_activeSlider == SliderLod) Shader.SetGlobalFloat(IdBarkLod, _lowBark ? _barkLod : 0f);
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, lw, row), "Сітка", _text);
                    if (Button(new Rect(cx + 2f, y, cw * 0.5f - 4f, row), "budget", !_quality) && _quality) SetMeshPreset(false);
                    if (Button(new Rect(cx + cw * 0.5f + 2f, y, cw * 0.5f - 4f, row), "quality", _quality) && !_quality) SetMeshPreset(true);
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, w, row * 1.4f), "LOD кори діє лише з LowEnd-корою; зміна сітки перебудовує дерево.", _hint);
                    break;
                }
                case 4: // View
                {
                    if (Button(new Rect(x0, y, half, row), _leaves ? "Листя: показано" : "Листя: сховано", _leaves)) _leaves = !_leaves;
                    if (Button(new Rect(x0 + w * 0.5f + 4f, y, half, row), "Скинути вигляд")) { _zoom = 1f; _pan = Vector2.zero; }
                    y += row + pad;
                    if (Button(new Rect(x0, y, half, row), "Знімок (PNG)")) _snapshotRequested = true; // OnGUI runs inside the URP loop
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, w, row * 1.6f), "Щипок — зум; двома пальцями — зсув; подвійний тап по порожньому місцю — скинути.", _text);
                    break;
                }
                case 5: // Stats
                {
                    if (Button(new Rect(x0, y, w, row), _stats ? "Статистика: показано" : "Статистика: сховано", _stats)) _stats = !_stats;
                    y += row + pad;
                    GUI.Label(new Rect(x0, y, w, row * 2.4f), "CPU: головний / рендер-потік. GPU — з FrameTimingManager;\nна Mali частота GPU плаває (DVFS), тож це не K1.\nFPS обмежено 60.", _hint);
                    break;
                }
            }
        }

        /// <summary>PNG of the current view without the menu (camera into a render texture) to persistentDataPath.</summary>
        private void Snapshot()
        {
            int w = Screen.width, h = Screen.height;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            string name = "bonsai_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string path = System.IO.Path.Combine(Application.persistentDataPath, name);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            _lastSnapshot = path;
            Debug.Log("PA102XS_IT snapshot " + path);
            Toast("Знімок: " + name, 3f);
        }

        // ------------------------------------------------------------------ self-test (virtual touchscreen)

        private Touchscreen _vts;
        private readonly System.Collections.Generic.Dictionary<string, Rect> _rects = new System.Collections.Generic.Dictionary<string, Rect>();
        private int _vid = 20;
        private string _lastSnapshot = "";

        /// <summary>Virtual tap on the button drawn with this label in the last frame.</summary>
        private IEnumerator TapLabel(string label)
        {
            if (!_rects.TryGetValue(label, out Rect r))
            {
                Step("missing_button " + label);
                yield break;
            }
            yield return VTap(_vid++, r.center);
            yield return Frames(3);
        }

        private void VTouch(int id, ETouchPhase phase, Vector2 gui)
        {
            var s = new TouchState { touchId = id, phase = phase, position = new Vector2(gui.x, Screen.height - gui.y), pressure = 1f };
            InputSystem.QueueStateEvent(_vts, s);
        }

        private IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private IEnumerator Seconds(float s)
        {
            float end = _time + s;
            while (_time < end) yield return null;
        }

        private IEnumerator VTap(int id, Vector2 gui)
        {
            VTouch(id, ETouchPhase.Began, gui);
            yield return Frames(3);
            VTouch(id, ETouchPhase.Ended, gui);
            yield return Frames(3);
        }

        private IEnumerator WaitIdle()
        {
            float end = _time + 30f;
            while ((_tree.Busy || _growing || !_baked) && _time < end) yield return null;
        }

        private static void Step(string s) { Debug.Log("PA102XS_IT step=" + s); }

        private IEnumerator SelfTest()
        {
            _vts = InputSystem.AddDevice<Touchscreen>("PA102XS_SelfTest");
            yield return WaitIdle();
            yield return Seconds(1.5f);
            Step("menu_open baked=" + _baked + " Ng=" + _tree.Current.Ng + " leaves=" + _tree.DisplayedLeafCount);
            yield return Seconds(5f);

            yield return VTap(1, _menuBtn.center);
            yield return Seconds(1f);
            Step("menu_closed menuOpen=" + _menuOpen);
            yield return Seconds(5f);

            // Tap-cut: hold on the branch farthest from the screen centre (preview), then release.
            SketchFrame f = _tree.Current;
            int g = -1;
            Vector2 at = default;
            float far = -1f;
            for (int k = 0; k < f.Ng - 1; k++)
            {
                int c0 = (int)f.Gm[k * 8 + 5], cn = (int)f.Gm[k * 8 + 6];
                if (cn <= 0) continue;
                Vector3 sp = Screen3(f, SketchConst.OBR + (c0 + cn / 2) * 5 + 2);
                float d = Mathf.Abs(sp.x - Screen.width * 0.5f);
                if (d > far && sp.x > 0 && sp.x < Screen.width)
                {
                    far = d;
                    g = k;
                    at = new Vector2(sp.x, Screen.height - sp.y);
                }
            }
            int ng0 = f.Ng;
            VTouch(2, ETouchPhase.Began, at);
            yield return Seconds(0.5f);
            Step("highlight candidate=" + _candidate + " expected=" + g + " at=" + at.ToString("0"));
            yield return Seconds(4.5f);
            VTouch(2, ETouchPhase.Ended, at);
            yield return Frames(3);
            int cutsAfterTap = _cuts;
            yield return WaitIdle();
            Step("cut cuts=" + cutsAfterTap + " Ng " + ng0 + "->" + _tree.Current.Ng + " bakeFrames=" + _lastJobFrames);
            yield return Seconds(5f);

            // Growth slider: open the menu (tab "Tree"), drag t from 100 % to 40 %, hold, release (bake).
            yield return VTap(3, _menuBtn.center);
            yield return Frames(3);
            yield return VTap(4, _tab0.center);
            yield return Frames(3);
            float y = _sliderTRect.center.y;
            VTouch(5, ETouchPhase.Began, new Vector2(SliderX(_sliderTRect, _t), y));
            yield return Frames(2);
            for (int i = 1; i <= 30; i++)
            {
                VTouch(5, ETouchPhase.Moved, new Vector2(SliderX(_sliderTRect, Mathf.Lerp(1f, 0.4f, i / 30f)), y));
                yield return null;
            }
            yield return Seconds(0.5f);
            Step("scrub t=" + _t.ToString("0.00") + " scrubbing=" + _scrubbing + " woodTris=" + _woodTrisNoBake + " leaves=" + _tree.DisplayedLeafCount);
            yield return Seconds(4.5f);
            VTouch(5, ETouchPhase.Ended, new Vector2(SliderX(_sliderTRect, 0.4f), y));
            yield return Frames(3);
            yield return WaitIdle();
            Step("scrub_baked t=" + _t.ToString("0.00") + " baked=" + _baked + " bakeFrames=" + _lastJobFrames + " leaves=" + _tree.DisplayedLeafCount);
            yield return Seconds(5f);

            // Growth animation 0 -> 100 % over 10 s.
            yield return VTap(6, _growBtn.center);
            yield return Seconds(5f);
            Step("growing t=" + _t.ToString("0.00") + " growing=" + _growing);
            yield return WaitIdle();
            Step("grown t=" + _t.ToString("0.00") + " baked=" + _baked + " bakeFrames=" + _lastJobFrames);
            yield return Seconds(3f);

            // Pinch zoom (two virtual fingers apart), then double tap on empty sky resets the view.
            Rect tr = TreeRect();
            Vector2 c = tr.center;
            for (int i = 0; i <= 20; i++)
            {
                float d = Mathf.Lerp(60f, 120f, i / 20f) * _dp;
                ETouchPhase ph = i == 0 ? ETouchPhase.Began : ETouchPhase.Moved;
                VTouch(7, ph, c + new Vector2(-d, 0f));
                VTouch(8, ph, c + new Vector2(d, 0f));
                yield return null;
            }
            VTouch(7, ETouchPhase.Ended, c + new Vector2(-120f * _dp, 0f));
            VTouch(8, ETouchPhase.Ended, c + new Vector2(120f * _dp, 0f));
            yield return Seconds(1f);
            Step("zoom zoom=" + _zoom.ToString("0.00"));
            yield return Seconds(4f);
            Vector2 sky = new Vector2(tr.x + 20f * _dp, tr.y + 20f * _dp);
            yield return VTap(9, sky);
            yield return VTap(10, sky);
            yield return Seconds(1f);
            Step("zoom_reset zoom=" + _zoom.ToString("0.00") + " cuts=" + _cuts);
            yield return Seconds(3f);

            // Quality tab: LowEnd bark + quality mesh preset (rebuild), then back to the defaults.
            yield return TapLabel("Якість");
            yield return TapLabel("LowEnd");
            yield return TapLabel("quality");
            yield return WaitIdle();
            yield return Seconds(1f);
            Step("quality lowBark=" + _lowBark + " quality=" + _quality + " woodTris=" + _tree.LastJobStats.WoodTris + " bakeFrames=" + _lastJobFrames);
            yield return Seconds(4f);
            yield return TapLabel("budget");
            yield return TapLabel("Повна");
            yield return WaitIdle();
            Step("quality_back lowBark=" + _lowBark + " quality=" + _quality + " woodTris=" + _tree.LastJobStats.WoodTris);

            // View tab: hide leaves, snapshot PNG; wind tab: off.
            yield return TapLabel("Вигляд");
            yield return TapLabel("Листя: показано");
            yield return TapLabel("Знімок (PNG)");
            yield return Frames(5);
            yield return TapLabel("Вітер");
            yield return TapLabel("Вітер: увімк.");
            yield return Seconds(1f);
            Step("view leaves=" + _leaves + " wind=" + _wind + " snapshot=" + _lastSnapshot + " exists=" + System.IO.File.Exists(_lastSnapshot));
            yield return Seconds(4f);
            yield return TapLabel("Вітер: вимк.");
            yield return TapLabel("Вигляд");
            yield return TapLabel("Листя: сховано");

            // Tree tab: another species, then back to the default tree.
            yield return TapLabel("Дерево");
            yield return TapLabel("Куромацу");
            yield return WaitIdle();
            yield return Seconds(1f);
            Step("species sp=" + Species[_sp] + " leaves=" + _tree.DisplayedLeafCount + " bakeFrames=" + _lastJobFrames + " wind=" + _wind + " leavesOn=" + _leaves);
            yield return Seconds(4f);
            yield return TapLabel("Момідзі");
            yield return WaitIdle();
            InputSystem.RemoveDevice(_vts);
            Step("done");
        }
    }
}
