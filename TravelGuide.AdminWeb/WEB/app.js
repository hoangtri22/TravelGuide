/**
 * Admin SPA: gọi Minimal API (/api/*), Bearer token, bảng POI, form lưu, duyệt/từ chối, dịch, tài khoản.
 * Dữ liệu POI khớp PoiDto server (camelCase JSON).
 */
let token = "";
let currentRole = "";
let pois = [];
let touristOverview = null;
let commentSearchTimer = null;
let visitHistoryLineChart = null;
const visitHistoryLineHidden = new Set();
let touristWeekVisitsChart = null;
/** Monday 00:00 UTC của tuần đang xem (biểu đồ lượt truy cập tuần). */
let touristWeekVisitsMondayMs = null;
/** yyyy-MM-dd (UTC) — gửi lên API để server gom đủ lịch sử (không giới hạn 300 dòng). */
let touristWeekChartMondayParam = null;
const TOURIST_WEEK_VISIT_DAYS = ["T2", "T3", "T4", "T5", "T6", "T7", "CN"];
const commentState = {
  status: "all",
  search: "",
  page: 1,
  pageSize: 8,
  totalItems: 0
};

const qs = (s) => document.querySelector(s);
const byId = (id) => document.getElementById(id);

function syncSidebarAvatar(displayName) {
  const avatar = document.querySelector(".sidebar-avatar");
  if (!avatar) return;
  const t = String(displayName || "").trim();
  avatar.textContent = t ? t[0].toUpperCase() : "A";
}

/** fetch JSON có gắn Authorization khi đã đăng nhập */
const api = async (url, options = {}) => {
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(url, { ...options, headers });
  if (!res.ok) {
    const text = (await res.text()).trim();
    if (res.status === 403) throw new Error(text || "Bạn không có quyền thực hiện thao tác này.");
    if (res.status === 401) throw new Error(text || "Phiên đăng nhập không hợp lệ.");
    throw new Error(text || `Lỗi ${res.status}`);
  }
  if (res.status === 204) return null;
  const ct = res.headers.get("content-type") || "";
  return ct.includes("application/json") ? res.json() : res.text();
};

/** fetch multipart (upload file), vẫn gắn Authorization */
const apiMultipart = async (url, formData, options = {}) => {
  const headers = { ...(options.headers || {}) };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(url, { ...options, method: options.method || "POST", headers, body: formData });
  if (!res.ok) {
    const text = (await res.text()).trim();
    if (res.status === 403) throw new Error(text || "Bạn không có quyền thực hiện thao tác này.");
    if (res.status === 401) throw new Error(text || "Phiên đăng nhập không hợp lệ.");
    throw new Error(text || `Lỗi ${res.status}`);
  }
  const ct = res.headers.get("content-type") || "";
  return ct.includes("application/json") ? res.json() : res.text();
};

/** Tiêu đề main — khớp nhãn trong Admin portal design / AppSidebar */
const TAB_PAGE_TITLES = {
  poiTab: "Quản lý địa điểm",
  commentTab: "Quản lí bình luận",
  translationTab: "Đa ngôn ngữ",
  audioTab: "Nguồn audio",
  accountTab: "Tài khoản web",
  touristTab: "Dữ liệu du khách",
  heatmapTab: "Heatmap"
};

/** Ẩn/hiện tab nội dung + trạng thái nút sidebar + subtitle + dải thống kê (chỉ tab POI) */
function switchTab(tabId) {
  document.querySelectorAll(".tab-content").forEach(el => el.classList.add("hidden"));
  document.querySelectorAll(".tab").forEach(el => el.classList.remove("active"));
  const panel = byId(tabId);
  if (panel) panel.classList.remove("hidden");
  const tabBtn = document.querySelector(`[data-tab='${tabId}']`);
  if (tabBtn) tabBtn.classList.add("active");
  const pt = byId("pageTitle");
  if (pt && TAB_PAGE_TITLES[tabId]) pt.textContent = TAB_PAGE_TITLES[tabId];
  const sub = byId("pageSubtitle");
  if (sub) {
    if (tabId === "poiTab") {
      sub.textContent = normalizedRole() === "admin"
        ? "Quản lý tất cả các địa điểm tham quan"
        : "Quản lý địa điểm của bạn";
    } else if (tabId === "commentTab") {
      sub.textContent = "Theo dõi và xử lý bình luận từ du khách.";
    } else if (tabId === "translationTab") {
      sub.textContent = "Chỉnh sửa bản dịch EN, JA, KO, ZH cho từng địa điểm.";
    } else if (tabId === "audioTab") {
      sub.textContent = "Gán hoặc upload audio và liên kết bản đồ cho POI đang hiển thị.";
    } else if (tabId === "accountTab") {
      sub.textContent = "Duyệt đăng ký chủ quán, khóa / xóa tài khoản web.";
    } else if (tabId === "touristTab") {
      sub.textContent = "Lượt truy cập và hoạt động du khách từ ứng dụng.";
    } else if (tabId === "heatmapTab") {
      sub.textContent = "";
    }
  }
  const strip = byId("statsStrip");
  if (strip) strip.classList.toggle("hidden", tabId !== "poiTab");
}

/** Chuẩn hóa role từ API (DB có thể khác hoa/thường). */
function normalizedRole() {
  return String(currentRole || "").trim().toLowerCase();
}

/** Sau đăng nhập: admin vs chủ quán — nút/tab, tiêu đề, gợi ý POI. */
function applyRoleChrome() {
  const r = normalizedRole();
  document.querySelectorAll(".admin-only").forEach(el => {
    el.classList.toggle("hidden", r !== "admin");
  });
  document.querySelectorAll(".owner-only").forEach(el => {
    el.classList.toggle("hidden", r !== "owner");
  });
  const brandSub = byId("brandSubtitle");
  if (brandSub) {
    brandSub.textContent = "Cổng quản trị";
  }
  const poiHead = byId("poiTabHeading");
  if (poiHead) {
    poiHead.textContent = r === "admin" ? "Tất cả địa điểm" : "Địa điểm của quán";
  }
  const newPoiLink = byId("newPoiBtn");
  if (newPoiLink) {
    const canCreate = r === "admin" || r === "owner";
    newPoiLink.classList.toggle("hidden", !canCreate);
    if (canCreate) newPoiLink.setAttribute("href", "poi-create.html");
    else newPoiLink.removeAttribute("href");
  }
  switchTab("poiTab");
}

/** Bảng chỉnh Audio URL / Map link theo danh sách POI hiện tại (chủ quán). */
function renderAudioSources() {
  const tbody = byId("audioTable")?.querySelector("tbody");
  if (!tbody) return;
  tbody.innerHTML = "";
  for (const p of pois) {
    const tr = document.createElement("tr");
    const tdId = document.createElement("td");
    tdId.textContent = String(p.id);
    const tdName = document.createElement("td");
    tdName.textContent = p.nameVi || "";
    const tdAudio = document.createElement("td");
    const inAudio = document.createElement("input");
    inAudio.type = "text";
    inAudio.className = "audio-src-input";
    inAudio.dataset.poiId = String(p.id);
    inAudio.value = p.audioUrl || "";
    inAudio.placeholder = "https://.../audio.mp3";
    tdAudio.appendChild(inAudio);
    const tdMap = document.createElement("td");
    const inMap = document.createElement("input");
    inMap.type = "text";
    inMap.className = "audio-map-input";
    inMap.dataset.poiId = String(p.id);
    inMap.value = p.mapLink || "";
    inMap.placeholder = "https://maps...";
    tdMap.appendChild(inMap);
    const tdAct = document.createElement("td");
    tdAct.className = "audio-actions-cell";
    const actionsWrap = document.createElement("div");
    actionsWrap.className = "audio-actions";
    const langSelect = document.createElement("select");
    langSelect.className = "audio-lang-select";
    langSelect.dataset.poiId = String(p.id);
    langSelect.innerHTML = `
      <option value="">Ngôn ngữ</option>
      <option value="vi">VI</option>
      <option value="en">EN</option>
      <option value="ja">JA</option>
      <option value="ko">KO</option>
      <option value="zh">ZH</option>`;
    actionsWrap.appendChild(langSelect);

    const inFile = document.createElement("input");
    inFile.type = "file";
    inFile.className = "audio-file-input";
    inFile.dataset.poiId = String(p.id);
    inFile.accept = ".mp3,.wav,.m4a,.aac,.ogg,audio/*";
    actionsWrap.appendChild(inFile);

    const upload = document.createElement("button");
    upload.type = "button";
    upload.className = "secondary audio-upload-btn";
    upload.dataset.poiId = String(p.id);
    upload.textContent = "Upload";

    const save = document.createElement("button");
    save.type = "button";
    save.className = "secondary audio-save-btn";
    save.dataset.poiId = String(p.id);
    save.textContent = "Lưu";
    actionsWrap.append(upload, save);
    tdAct.appendChild(actionsWrap);
    tr.append(tdId, tdName, tdAudio, tdMap, tdAct);
    tbody.appendChild(tr);
  }
}

function setAuthPanel(mode) {
  const loginPanel = byId("loginPanel");
  const registerPanel = byId("registerPanel");
  const tabLogin = byId("authTabLogin");
  const tabReg = byId("authTabRegister");
  const hint = document.querySelector(".login-hint");
  const isLogin = mode === "login";
  if (loginPanel) loginPanel.classList.toggle("hidden", !isLogin);
  if (registerPanel) registerPanel.classList.toggle("hidden", isLogin);
  if (tabLogin) {
    tabLogin.classList.toggle("active", isLogin);
    tabLogin.setAttribute("aria-selected", isLogin ? "true" : "false");
  }
  if (tabReg) {
    tabReg.classList.toggle("active", !isLogin);
    tabReg.setAttribute("aria-selected", isLogin ? "false" : "true");
  }
  if (hint) hint.classList.toggle("hidden", !isLogin);
}

const fmtDate = (v) => {
  if (!v) return "";
  const raw = String(v).trim();
  const hasZone = /([zZ]|[+\-]\d{2}:\d{2})$/.test(raw);
  // DateTime từ .NET/SQL đôi khi không có timezone suffix; coi như UTC để đổi đúng sang giờ VN.
  const normalized = hasZone ? raw : `${raw}Z`;
  const d = new Date(normalized);
  return Number.isNaN(d.getTime())
    ? raw
    : d.toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" });
};

const fmtDateUtc = (v) => {
  if (!v) return "";
  const raw = String(v).trim();
  const hasZone = /([zZ]|[+\-]\d{2}:\d{2})$/.test(raw);
  const normalized = hasZone ? raw : `${raw}Z`;
  const d = new Date(normalized);
  if (Number.isNaN(d.getTime())) return raw;
  const dd = String(d.getUTCDate()).padStart(2, "0");
  const mm = String(d.getUTCMonth() + 1).padStart(2, "0");
  const yyyy = d.getUTCFullYear();
  const hh = String(d.getUTCHours()).padStart(2, "0");
  const mi = String(d.getUTCMinutes()).padStart(2, "0");
  const ss = String(d.getUTCSeconds()).padStart(2, "0");
  return `${hh}:${mi}:${ss} ${dd}/${mm}/${yyyy}`;
};

const visitHistoryState = { sortKey: "id", sortDir: -1, page: 1, pageSize: 10, query: "", userFilter: "" };
/** yyyy-MM-dd (UTC calendar) — null = 7 ngày gần nhất theo server */
const visitHistoryChartState = { customWeekStart: null };

/** Leaflet + heatmap tab (admin). */
let heatmapLeafletPromise = null;
let heatmapMap = null;
let heatmapBaseTiles = null;
let heatmapLayer = null;
let heatmapGeofenceLayer = null;
let heatmapMarkers = null;
/** Copy latlngs cho nút “Vừa khung” sau fitBounds. */
let heatmapLastFitLatLngs = null;

const HEATMAP_MAX_ZOOM = 22;
const HEATMAP_TILE_NATIVE_ZOOM = 19;

/** Cho phép zoom “ảo” sau mức tile OSM (19): ảnh phóng thêm nhưng vẫn dùng được. */
function heatmapApplyExtendedZoomCapabilities(map) {
  if (!map) return;
  try {
    map.setMaxZoom(HEATMAP_MAX_ZOOM);
  } catch { /* ignore */ }
  const patchTiles = (layer) => {
    if (!layer || typeof layer !== "object") return;
    try {
      layer.options.maxZoom = HEATMAP_MAX_ZOOM;
      layer.options.maxNativeZoom = HEATMAP_TILE_NATIVE_ZOOM;
    } catch { /* ignore */ }
  };
  if (heatmapBaseTiles) patchTiles(heatmapBaseTiles);
  map.eachLayer((ly) => {
    if (ly instanceof L.TileLayer) patchTiles(ly);
  });
}

/** Toolbar zoom / fit — luôn khởi tạo map nếu chưa có (tránh click khi heatmapMap === null). */
function heatmapRunWhenReady(fn) {
  if (normalizedRole() !== "admin") return Promise.resolve();
  return ensureLeafletHeatLibs()
    .then(() => {
      if (heatmapMap) {
        fn();
        return;
      }
      return refreshHeatmapTab().then(() => {
        fn();
      });
    })
    .catch((err) => {
      alert(err?.message || String(err));
    });
}

/**
 * Zoom bằng bánh xe trong `.main-content { overflow:auto }`:
 * Chrome có thể làm listener wheel của Leaflet thành passive → không zoom được.
 */
/** Pane: geofence (giữa) · chấm POI (trên quầng nhiệt). */
function heatmapEnsureHeatmapPanes(map) {
  if (!map?.createPane) return;
  if (!map.getPane("tgHeatGeofence")) {
    map.createPane("tgHeatGeofence");
    map.getPane("tgHeatGeofence").style.zIndex = "455";
  }
  if (!map.getPane("tgHeatDots")) {
    map.createPane("tgHeatDots");
    map.getPane("tgHeatDots").style.zIndex = "480";
  }
}

/** Bán kính geofence POI (m) — trùng field Radius trên CMS / extra_places. */
function poiRadiusMeters(p) {
  const r = Number(p?.radius ?? p?.Radius);
  return Number.isFinite(r) && r > 0 ? r : 50;
}

/** Bán kính marker (px) theo độ “nhiệt”, không cho hai vòng tròn chồng nhau (khoảng cách mép ≥ gapPx). */
function heatmapCircleRadiusPx(desiredPx, neighborDistPx, gapPx, minPx, maxPx) {
  const cap =
    neighborDistPx === Infinity || !Number.isFinite(neighborDistPx)
      ? maxPx
      : Math.max(minPx, (neighborDistPx - gapPx) / 2);
  return Math.min(maxPx, Math.max(minPx, Math.min(desiredPx, cap)));
}

function bindHeatmapWheelZoomFix(map) {
  const el = map?.getContainer?.();
  if (!el || el.dataset.tgWheelFix === "1") return;
  el.dataset.tgWheelFix = "1";
  let acc = 0;
  el.addEventListener(
    "wheel",
    (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      let dy = ev.deltaY;
      if (ev.deltaMode === 1) dy *= 16;
      else if (ev.deltaMode === 2) dy *= 800;
      acc += dy;
      const stepAt = 48;
      if (Math.abs(acc) < stepAt) return;
      const dir = acc > 0 ? -1 : 1;
      acc = 0;
      const z = map.getZoom();
      const cap = typeof map.getMaxZoom === "function" ? map.getMaxZoom() : HEATMAP_MAX_ZOOM;
      const nz = Math.max(map.getMinZoom(), Math.min(cap, z + dir));
      if (nz !== z) map.setZoom(nz);
    },
    { passive: false, capture: true }
  );
}

function heatmapPick(row, camel, pascal) {
  if (!row || typeof row !== "object") return undefined;
  if (Object.prototype.hasOwnProperty.call(row, camel)) return row[camel];
  if (Object.prototype.hasOwnProperty.call(row, pascal)) return row[pascal];
  return undefined;
}

function loadExternalCss(href) {
  return new Promise((resolve, reject) => {
    const el = document.createElement("link");
    el.rel = "stylesheet";
    el.href = href;
    el.onload = () => resolve();
    el.onerror = () => reject(new Error(`Không tải CSS: ${href}`));
    document.head.appendChild(el);
  });
}

function loadExternalScript(src) {
  return new Promise((resolve, reject) => {
    const el = document.createElement("script");
    el.src = src;
    el.async = true;
    el.onload = () => resolve();
    el.onerror = () => reject(new Error(`Không tải script: ${src}`));
    document.head.appendChild(el);
  });
}

function ensureLeafletHeatLibs() {
  if (typeof L !== "undefined" && typeof L.heatLayer === "function") return Promise.resolve();
  if (heatmapLeafletPromise) return heatmapLeafletPromise;
  heatmapLeafletPromise = (async () => {
    await loadExternalCss("https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.min.css");
    await loadExternalScript("https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.min.js");
    await loadExternalScript("https://cdnjs.cloudflare.com/ajax/libs/leaflet.heat/0.2.0/leaflet-heat.js");
  })();
  return heatmapLeafletPromise;
}

function poiCoordPick(p) {
  const lat = Number(p?.latitude ?? p?.Latitude);
  const lng = Number(p?.longitude ?? p?.Longitude);
  if (!Number.isFinite(lat) || !Number.isFinite(lng)) return null;
  if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;
  return { lat, lng };
}

/** Có dữ liệu trên bảng heatmap (QR tích lũy hoặc GPS 15′ hoặc QR trong ngày UTC). */
function heatmapPoiHasActivity(meta) {
  if (!meta) return false;
  const scans = Number(meta.scans) || 0;
  const g = Number(meta.recentGps) || 0;
  const q = Number(meta.qrToday) || 0;
  return scans > 0 || g > 0 || q > 0;
}

/** Trọng số nhiệt trên bản đồ: lớn nhất giữa QR tích lũy và (GPS gần đây + QR trong ngày). */
function heatmapPoiIntensity(meta) {
  if (!meta) return 0;
  const scans = Number(meta.scans) || 0;
  const live = (Number(meta.recentGps) || 0) + (Number(meta.qrToday) || 0);
  return Math.max(1, scans, live);
}

/** Chuẩn hóa tên POI (VI) để ghép log ↔ danh sách quản trị khi PoiId lệch DB. */
function heatmapNormalizeViName(s) {
  return String(s ?? "")
    .trim()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/\s+/g, " ");
}

/** Tên POI (đã chuẩn hóa) không hiển thị trên tab Heatmap — không xóa khỏi CMS/app. */
const HEATMAP_HIDDEN_POI_NAME_KEYS = new Set([
  heatmapNormalizeViName("Sủi cảo Tân Tòng Lợi"),
  heatmapNormalizeViName("Sủi cảo Tân Long Lợi")
]);

function heatmapIsHiddenOnHeatmapTab(p) {
  const k = heatmapNormalizeViName(p?.nameVi ?? p?.NameVi);
  return Boolean(k && HEATMAP_HIDDEN_POI_NAME_KEYS.has(k));
}

/** Map: tên đã chuẩn hóa → mảng POI (cùng tên có thể nhiều bản ghi). */
function heatmapBuildPoiNameLookup(poisArr) {
  const m = new Map();
  if (!Array.isArray(poisArr)) return m;
  for (const p of poisArr) {
    const k = heatmapNormalizeViName(p?.nameVi ?? p?.NameVi);
    if (!k) continue;
    if (!m.has(k)) m.set(k, []);
    m.get(k).push(p);
  }
  return m;
}

/**
 * Tìm POI trên bản đồ: ưu tiên Id; nếu log Id không có trong /api/pois thì thử đúng một POI trùng tên (VI).
 * @returns {{ p: object|null, how: "id"|"name"|"none"|"ambiguous" }}
 */
function heatmapResolvePoiForMap(poisArr, nameLookup, pid, displayNameVi) {
  const byId = poisArr.find((x) => Number(x?.id ?? x?.Id) === pid);
  if (byId) return { p: byId, how: "id" };
  const k = heatmapNormalizeViName(displayNameVi);
  if (!k) return { p: null, how: "none" };
  const hits = nameLookup.get(k);
  if (!hits || hits.length === 0) return { p: null, how: "none" };
  if (hits.length > 1) return { p: null, how: "ambiguous" };
  return { p: hits[0], how: "name" };
}

/** Một dòng bảng heatmap sau khi gộp (nhiều PoiId / cùng tên → một địa điểm). */
function heatmapBuildAggregatedTableRows(poisArr, nameLookup, revRows, qrTodayPick) {
  const agg = new Map();
  for (const row of revRows) {
    const pid = Number(heatmapPick(row, "poiId", "PoiId"));
    if (!pid) continue;
    const scans = Number(heatmapPick(row, "scanCount", "ScanCount") ?? 0) || 0;
    const gps = Number(heatmapPick(row, "recentGpsHits", "RecentGpsHits") ?? 0) || 0;
    const qr = qrTodayPick(row);
    if (scans <= 0 && gps <= 0 && qr <= 0) continue;

    const logName = String(heatmapPick(row, "poiNameVi", "PoiNameVi") ?? "");
    if (HEATMAP_HIDDEN_POI_NAME_KEYS.has(heatmapNormalizeViName(logName))) continue;
    const { p } = heatmapResolvePoiForMap(poisArr, nameLookup, pid, logName);
    const key = p ? `a:${Number(p.id ?? p.Id)}` : `n:${heatmapNormalizeViName(logName)}`;
    const label = p ? String(p?.nameVi ?? p?.NameVi ?? logName).trim() || logName : logName;

    const cur = agg.get(key);
    if (!cur) {
      agg.set(key, { label, scans, gps, qr });
    } else {
      cur.scans += scans;
      cur.gps += gps;
      cur.qr += qr;
    }
  }
  // Luôn hiển thị toàn bộ POI trên bảng heatmap (POI chưa có activity => 0/0/0).
  for (const p of poisArr) {
    const adminId = Number(p?.id ?? p?.Id);
    if (!Number.isFinite(adminId) || adminId <= 0) continue;
    const key = `a:${adminId}`;
    if (agg.has(key)) continue;
    const label = String(p?.nameVi ?? p?.NameVi ?? "").trim();
    agg.set(key, { label, scans: 0, gps: 0, qr: 0 });
  }
  return [...agg.values()];
}

async function refreshHeatmapTab() {
  const summaryEl = byId("heatmapSummary");
  const tbody = byId("heatmapPoiTbody");
  if (!summaryEl || !tbody) return;
  if (normalizedRole() !== "admin") return;

  summaryEl.textContent = "Đang tải…";
  tbody.innerHTML = "";

  await ensureLeafletHeatLibs();
  if (typeof L === "undefined" || typeof L.heatLayer !== "function") {
    summaryEl.textContent = "Không tải được thư viện bản đồ.";
    return;
  }

  let dashboard;
  try {
    if (!Array.isArray(pois) || pois.length === 0) await loadPois();
    dashboard = await api("/api/tourists/poi-scan-dashboard");
  } catch (err) {
    summaryEl.textContent = err?.message || String(err);
    return;
  }

  const revRows = dashboard?.revenueByPoi ?? dashboard?.RevenueByPoi ?? [];
  const totalScans = Number(dashboard?.totalScans ?? dashboard?.TotalScans ?? 0) || 0;
  const grand = Number(dashboard?.grandTotalVnd ?? dashboard?.GrandTotalVnd ?? 0) || 0;
  const heatWin =
    Number(dashboard?.heatmapRecentWindowMinutes ?? dashboard?.HeatmapRecentWindowMinutes ?? 15) || 15;
  const qrDayUtc =
    String(dashboard?.heatmapQrDayUtc ?? dashboard?.HeatmapQrDayUtc ?? "").trim() ||
    new Date().toISOString().slice(0, 10);

  const poisHeat = Array.isArray(pois) ? pois.filter((p) => !heatmapIsHiddenOnHeatmapTab(p)) : [];

  const heatmapQrTodayPick = (row) => {
    const v = heatmapPick(row, "qrScansTodayUtc", "QrScansTodayUtc");
    if (v !== undefined && v !== null) return Number(v) || 0;
    return Number(heatmapPick(row, "recentQrScans", "RecentQrScans") ?? 0) || 0;
  };

  const scanByPoi = new Map();
  for (const row of revRows) {
    const pid = Number(heatmapPick(row, "poiId", "PoiId"));
    if (!pid) continue;
    scanByPoi.set(pid, {
      name: String(heatmapPick(row, "poiNameVi", "PoiNameVi") ?? ""),
      scans: Number(heatmapPick(row, "scanCount", "ScanCount") ?? 0) || 0,
      vnd: Number(heatmapPick(row, "totalVnd", "TotalVnd") ?? 0) || 0,
      recentGps: Number(heatmapPick(row, "recentGpsHits", "RecentGpsHits") ?? 0) || 0,
      qrToday: heatmapQrTodayPick(row)
    });
  }

  const nameLookup = heatmapBuildPoiNameLookup(poisHeat);
  /** Ghép nhiều PoiId trong log → cùng một POI quản trị: cộng trọng số nhiệt. */
  const plate = new Map();
  let maxIntensity = 1;
  let cntId = 0;
  let cntName = 0;
  let cntNone = 0;
  let cntAmbiguous = 0;
  let cntNoCoord = 0;
  for (const row of revRows) {
    const pid = Number(heatmapPick(row, "poiId", "PoiId"));
    if (!pid) continue;
    const meta = scanByPoi.get(pid);
    if (!heatmapPoiHasActivity(meta)) continue;
    const displayName = String(heatmapPick(row, "poiNameVi", "PoiNameVi") ?? meta?.name ?? "");
    const { p, how } = heatmapResolvePoiForMap(poisHeat, nameLookup, pid, displayName);
    if (how === "id") cntId += 1;
    else if (how === "name") cntName += 1;
    else if (how === "ambiguous") cntAmbiguous += 1;
    else cntNone += 1;
    if (!p) continue;
    const c = poiCoordPick(p);
    if (!c) {
      cntNoCoord += 1;
      continue;
    }
    const w = heatmapPoiIntensity(meta);
    const adminId = Number(p?.id ?? p?.Id);
    if (!Number.isFinite(adminId) || adminId <= 0) continue;
    const nm = String(p?.nameVi ?? p?.NameVi ?? displayName ?? "");
    const prevCell = plate.get(adminId);
    if (!prevCell) {
      plate.set(adminId, {
        lat: c.lat,
        lng: c.lng,
        w,
        scans: Number(meta.scans) || 0,
        recentGps: Number(meta.recentGps) || 0,
        qrToday: Number(meta.qrToday) || 0,
        vnd: Number(meta.vnd) || 0,
        name: nm
      });
    } else {
      prevCell.w += w;
      prevCell.scans += Number(meta.scans) || 0;
      prevCell.recentGps += Number(meta.recentGps) || 0;
      prevCell.qrToday += Number(meta.qrToday) || 0;
      prevCell.vnd += Number(meta.vnd) || 0;
    }
    maxIntensity = Math.max(maxIntensity, plate.get(adminId).w);
  }

  // Bổ sung POI chưa có log để map/tab luôn hiện đủ tất cả địa điểm.
  for (const p of poisHeat) {
    const adminId = Number(p?.id ?? p?.Id);
    if (!Number.isFinite(adminId) || adminId <= 0) continue;
    if (plate.has(adminId)) continue;
    const c = poiCoordPick(p);
    if (!c) continue;
    const nm = String(p?.nameVi ?? p?.NameVi ?? "").trim();
    plate.set(adminId, {
      lat: c.lat,
      lng: c.lng,
      w: 1,
      scans: 0,
      recentGps: 0,
      qrToday: 0,
      vnd: 0,
      name: nm
    });
    maxIntensity = Math.max(maxIntensity, 1);
  }
  const heatPoints = [...plate.values()].map((v) => [v.lat, v.lng, v.w]);

  const hintParts = [];
  if (cntName > 0) hintParts.push(`${cntName} điểm ghép theo tên (PoiId log ≠ quản trị)`);
  if (cntAmbiguous > 0) hintParts.push(`${cntAmbiguous} dòng bỏ qua: trùng tên nhiều POI — sửa Id trong log hoặc đổi tên POI`);
  if (cntNone > 0) hintParts.push(`${cntNone} dòng không có POI khớp Id/tên`);
  if (cntNoCoord > 0) hintParts.push(`${cntNoCoord} POI thiếu lat/lng hợp lệ`);
  const hintMissing = hintParts.length ? ` (${hintParts.join("; ")}.)` : "";

  // Ẩn dòng summary dài phía trên heatmap theo yêu cầu UI hiện tại.
  summaryEl.textContent = "";

  const mapEl = byId("heatmapMap");
  if (!mapEl) return;

  if (!heatmapMap) {
    heatmapMap = L.map(mapEl, {
      // Wheel zoom do bindHeatmapWheelZoomFix (passive:false); Leaflet scrollWheelZoom hay fail trong khối cuộn.
      scrollWheelZoom: false,
      doubleClickZoom: true,
      boxZoom: true,
      keyboard: true,
      zoomControl: true,
      minZoom: 3,
      maxZoom: HEATMAP_MAX_ZOOM
    }).setView([16.05, 108.2], 6);
    heatmapBaseTiles = L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: HEATMAP_MAX_ZOOM,
      maxNativeZoom: HEATMAP_TILE_NATIVE_ZOOM,
      attribution: "&copy; OpenStreetMap"
    }).addTo(heatmapMap);
    const hc = heatmapMap.getContainer();
    if (typeof L !== "undefined" && L.DomEvent) {
      L.DomEvent.disableScrollPropagation(hc);
      // Không gọi disableClickPropagation — dễ làm nút zoom mặc định của Leaflet “không ăn”.
    }
    bindHeatmapWheelZoomFix(heatmapMap);
  } else {
    try {
      heatmapMap.scrollWheelZoom.disable();
    } catch { /* ignore */ }
    heatmapApplyExtendedZoomCapabilities(heatmapMap);
    if (!heatmapBaseTiles) {
      heatmapMap.eachLayer((ly) => {
        if (!heatmapBaseTiles && ly instanceof L.TileLayer) heatmapBaseTiles = ly;
      });
    }
    bindHeatmapWheelZoomFix(heatmapMap);
  }

  if (heatmapLayer) {
    heatmapMap.removeLayer(heatmapLayer);
    heatmapLayer = null;
  }
  if (heatmapGeofenceLayer) {
    heatmapMap.removeLayer(heatmapGeofenceLayer);
    heatmapGeofenceLayer = null;
  }
  if (heatmapMarkers) {
    heatmapMap.removeLayer(heatmapMarkers);
    heatmapMarkers = null;
  }

  if (heatPoints.length > 0) {
    heatmapEnsureHeatmapPanes(heatmapMap);
    heatmapLayer = L.heatLayer(heatPoints, {
      // Giảm blur để các quầng nhiệt ít “ăn” vào nhau; radius vừa đủ nhìn.
      radius: 26,
      blur: 14,
      maxZoom: HEATMAP_MAX_ZOOM,
      max: maxIntensity,
      gradient: { 0.4: "blue", 0.55: "cyan", 0.7: "lime", 0.85: "yellow", 1: "red" }
    }).addTo(heatmapMap);

    const latlngs = heatPoints.map(([la, lo]) => L.latLng(la, lo));
    heatmapLastFitLatLngs = latlngs.slice();
    try {
      heatmapMap.fitBounds(L.latLngBounds(latlngs), { padding: [28, 28], maxZoom: 17 });
    } catch { /* ignore */ }
    try {
      heatmapMap.invalidateSize(false);
    } catch { /* ignore */ }

    const plateArr = [...plate.values()];
    const pts = plateArr.map((v) => ({
      v,
      ll: L.latLng(v.lat, v.lng),
      pt: heatmapMap.latLngToContainerPoint(L.latLng(v.lat, v.lng))
    }));

    const gapPx = 8;
    const rMin = 10;
    const rMaxSingle = 30;
    const rMaxDense = 26;

    heatmapMarkers = L.layerGroup();
    for (let i = 0; i < pts.length; i++) {
      const { v, ll } = pts[i];
      let minNeighborPx = Infinity;
      for (let j = 0; j < pts.length; j++) {
        if (i === j) continue;
        const dx = pts[i].pt.x - pts[j].pt.x;
        const dy = pts[i].pt.y - pts[j].pt.y;
        const d = Math.hypot(dx, dy);
        if (d > 0.5 && d < minNeighborPx) minNeighborPx = d;
      }
      if (pts.length <= 1) minNeighborPx = Infinity;

      const desired = 13 + Math.min(22, Math.sqrt(Math.max(1, v.w)) * 2.2);
      const radius = heatmapCircleRadiusPx(
        desired,
        minNeighborPx,
        gapPx,
        rMin,
        pts.length <= 1 ? rMaxSingle : rMaxDense
      );

      const nameVi = String(v.name || "");
      const m = L.circleMarker(ll, {
        pane: "tgHeatDots",
        radius,
        stroke: true,
        weight: 2,
        color: "rgba(15,23,42,0.58)",
        fillColor: "#0d9488",
        fillOpacity: 0.45
      });
      m.bindPopup(
        `<strong>${escCell(nameVi)}</strong><br><span class="hint">(Có thể gộp nhiều PoiId trong log về cùng POI)</span><br>Lượt quét QR (tích lũy, cộng): <b>${v.scans.toLocaleString("vi-VN")}</b><br>GPS ${heatWin}′: <b>${v.recentGps.toLocaleString("vi-VN")}</b> · QR ngày UTC ${escCell(qrDayUtc)}: <b>${v.qrToday.toLocaleString("vi-VN")}</b><br>Độ nhiệt bản đồ: <b>${v.w.toLocaleString("vi-VN")}</b><br>Doanh thu (cộng): ${v.vnd.toLocaleString("vi-VN")}đ`
      );
      if (nameVi) m.bindTooltip(nameVi, { sticky: true, direction: "top" });
      m.addTo(heatmapMarkers);
    }
    heatmapMarkers.addTo(heatmapMap);
  } else {
    heatmapLastFitLatLngs = null;
    heatmapMap.setView([16.05, 108.2], 6);
  }

  // Vòng tròn geofence (mét): khoảng cách từ tâm POI mà app có thể bắt đầu thuyết minh (sau debounce trong app).
  if (Array.isArray(poisHeat) && poisHeat.length > 0) {
    heatmapEnsureHeatmapPanes(heatmapMap);
    heatmapGeofenceLayer = L.layerGroup();
    for (const p of poisHeat) {
      const c = poiCoordPick(p);
      if (!c) continue;
      const rM = poiRadiusMeters(p);
      const ll = L.latLng(c.lat, c.lng);
      const nameVi = String(p?.nameVi ?? p?.NameVi ?? "").trim() || `POI #${p?.id ?? ""}`;
      const ring = L.circle(ll, {
        pane: "tgHeatGeofence",
        radius: rM,
        stroke: true,
        color: "#6d28d9",
        weight: 2,
        dashArray: "8 7",
        fillColor: "#a855f7",
        fillOpacity: 0.06
      });
      ring.bindTooltip(`Vùng thuyết minh: ${rM.toLocaleString("vi-VN")} m — ${nameVi}`, { sticky: true });
      ring.bindPopup(
        `<strong>${escCell(nameVi)}</strong><br>Bán kính geofence: <b>${rM.toLocaleString("vi-VN")} m</b><br><span class="hint">App coi là “trong điểm” khi GPS nằm trong vòng này (còn debounce ~3 giây và cooldown trong GeofenceEngine).</span>`
      );
      ring.addTo(heatmapGeofenceLayer);
    }
    heatmapGeofenceLayer.addTo(heatmapMap);
  }

  requestAnimationFrame(() => {
    try {
      heatmapMap?.invalidateSize();
    } catch { /* ignore */ }
  });

  const tableRows = heatmapBuildAggregatedTableRows(poisHeat, nameLookup, revRows, heatmapQrTodayPick);
  const sorted = [...tableRows].sort((a, b) => {
    if (b.gps !== a.gps) return b.gps - a.gps;
    if (b.qr !== a.qr) return b.qr - a.qr;
    return b.scans - a.scans;
  });
  tbody.innerHTML = "";
  if (sorted.length === 0) {
    const tr = document.createElement("tr");
    const td = document.createElement("td");
    td.colSpan = 2;
    td.className = "hint";
    td.textContent = "Chưa có dòng thống kê theo POI trong log.";
    tr.appendChild(td);
    tbody.appendChild(tr);
    return;
  }
  for (const r of sorted) {
    const tr = document.createElement("tr");
    tr.innerHTML = `<td>${escCell(r.label)}</td><td>${r.gps.toLocaleString("vi-VN")}</td>`;
    tbody.appendChild(tr);
  }
}

function visitHistoryUserClass(username) {
  const u = String(username || "").trim().toLowerCase();
  if (u === "phwnii") return "vh-user-phwnii";
  if (u === "test") return "vh-user-test";
  return "vh-user-generic";
}

function buildVisitHistoryRows(historyRows) {
  const arr = Array.isArray(historyRows) ? historyRows : [];
  return arr.map((x) => {
    const id = String(touristPick(x, "id", "Id") ?? "");
    const user = String(touristPick(x, "username", "Username") ?? "");
    const poiId = touristPick(x, "poiId", "PoiId");
    const poiName = String(touristPick(x, "poiNameVi", "PoiNameVi") ?? "");
    const event = String(touristPick(x, "eventType", "EventType") ?? "");
    const seconds = Number(touristPick(x, "playbackSeconds", "PlaybackSeconds") ?? 0) || 0;
    const pct = Number(touristPick(x, "watchedPercent", "WatchedPercent") ?? 0) || 0;
    const occurredRaw = touristPick(x, "occurredAtUtc", "OccurredAtUtc");
    const ts = occurredRaw ? new Date(occurredRaw).getTime() : 0;
    return {
      id,
      user,
      poi: `${poiId} — ${poiName}`,
      event,
      seconds,
      pct,
      occurred: fmtDate(occurredRaw),
      ts: Number.isFinite(ts) ? ts : 0
    };
  });
}

function ensureVisitHistoryEventsBound() {
  const root = byId("tourist-sec-visits");
  if (!root || root.dataset.vhBound === "1") return;
  root.dataset.vhBound = "1";

  byId("vhSearch")?.addEventListener("input", (e) => {
    visitHistoryState.query = String(e.target?.value || "");
    visitHistoryState.page = 1;
    renderVisitHistoryExplorer(window.__tgVisitHistoryRows || []);
  });
  byId("vhUserFilter")?.addEventListener("change", (e) => {
    visitHistoryState.userFilter = String(e.target?.value || "");
    visitHistoryState.page = 1;
    renderVisitHistoryExplorer(window.__tgVisitHistoryRows || []);
  });
  byId("vhPageSize")?.addEventListener("change", (e) => {
    visitHistoryState.pageSize = Math.max(1, Number(e.target?.value || 10));
    visitHistoryState.page = 1;
    renderVisitHistoryExplorer(window.__tgVisitHistoryRows || []);
  });
  byId("vhTable")?.querySelectorAll("th[data-vh-sort]")?.forEach((th) => {
    th.addEventListener("click", () => {
      const key = th.getAttribute("data-vh-sort");
      if (!key) return;
      if (visitHistoryState.sortKey === key) visitHistoryState.sortDir *= -1;
      else {
        visitHistoryState.sortKey = key;
        visitHistoryState.sortDir = (key === "id" || key === "occurred") ? -1 : 1;
      }
      visitHistoryState.page = 1;
      renderVisitHistoryExplorer(window.__tgVisitHistoryRows || []);
    });
  });
}

function renderVisitHistoryExplorer(historyRows) {
  window.__tgVisitHistoryRows = historyRows;
  ensureVisitHistoryEventsBound();
  const rows = buildVisitHistoryRows(historyRows);

  const users = [...new Set(rows.map((r) => r.device).filter(Boolean))].sort((a, b) => a.localeCompare(b));
  const userSelect = byId("vhUserFilter");
  if (userSelect) {
    const selected = visitHistoryState.userFilter;
    userSelect.innerHTML = `<option value="">Tất cả user</option>${users.map((u) => `<option value="${escCell(u)}">${escCell(u)}</option>`).join("")}`;
    userSelect.value = users.includes(selected) ? selected : "";
    visitHistoryState.userFilter = userSelect.value;
  }

  const q = visitHistoryState.query.trim().toLowerCase();
  let filtered = rows.filter((r) => {
    const matchQ = !q || r.id.toLowerCase().includes(q) || r.user.toLowerCase().includes(q) || r.poi.toLowerCase().includes(q) || r.event.toLowerCase().includes(q);
    const matchU = !visitHistoryState.userFilter || r.user === visitHistoryState.userFilter;
    return matchQ && matchU;
  });

  filtered.sort((a, b) => {
    const key = visitHistoryState.sortKey;
    const dir = visitHistoryState.sortDir;
    if (key === "occurred") return (a.ts - b.ts) * dir;
    if (key === "seconds" || key === "pct") return ((a[key] || 0) - (b[key] || 0)) * dir;
    if (key === "id") return a.id.localeCompare(b.id, undefined, { numeric: true }) * dir;
    return String(a[key] || "").localeCompare(String(b[key] || "")) * dir;
  });

  const total = filtered.length;
  const pageSize = Math.max(1, Number(visitHistoryState.pageSize || 10));
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (visitHistoryState.page > pages) visitHistoryState.page = 1;
  const start = (visitHistoryState.page - 1) * pageSize;
  const view = filtered.slice(start, start + pageSize);

  const result = byId("vhResultCount");
  if (result) result.textContent = `${total} phiên`;

  const tbody = byId("vhTbody");
  if (tbody) {
    tbody.innerHTML = view.length
      ? view.map((r) => `
        <tr>
          <td class="vh-id">${escCell(r.id)}</td>
          <td><span class="vh-user-badge ${visitHistoryUserClass(r.user)}">${escCell(r.user)}</span></td>
          <td class="vh-poi">${escCell(r.poi)}</td>
          <td><span class="vh-event-pill">${escCell(r.event)}</span></td>
          <td class="vh-num">${r.seconds}</td>
          <td class="vh-num">${r.pct}</td>
          <td class="vh-occurred">${escCell(r.occurred)}</td>
        </tr>`).join("")
      : `<tr><td colspan="7" class="vh-no-data">Không có dữ liệu</td></tr>`;
  }

  const pageInfo = byId("vhPageInfo");
  if (pageInfo) {
    const from = total === 0 ? 0 : start + 1;
    const to = Math.min(start + pageSize, total);
    pageInfo.textContent = `Hiển thị ${from}–${to} / ${total} phiên`;
  }

  const pageBtns = byId("vhPageBtns");
  if (pageBtns) {
    let html = `<button ${visitHistoryState.page === 1 ? "disabled" : ""} data-vh-page="${visitHistoryState.page - 1}">‹</button>`;
    for (let p = 1; p <= pages; p++) {
      if (pages <= 6 || p === 1 || p === pages || Math.abs(p - visitHistoryState.page) <= 1) {
        html += `<button class="${p === visitHistoryState.page ? "active" : ""}" data-vh-page="${p}">${p}</button>`;
      } else if (Math.abs(p - visitHistoryState.page) === 2) {
        html += `<button disabled class="vh-ellipsis">…</button>`;
      }
    }
    html += `<button ${visitHistoryState.page === pages ? "disabled" : ""} data-vh-page="${visitHistoryState.page + 1}">›</button>`;
    pageBtns.innerHTML = html;
    pageBtns.querySelectorAll("button[data-vh-page]").forEach((btn) => {
      btn.addEventListener("click", () => {
        visitHistoryState.page = Number(btn.getAttribute("data-vh-page") || 1);
        renderVisitHistoryExplorer(window.__tgVisitHistoryRows || []);
      });
    });
  }

  byId("vhTable")?.querySelectorAll("th[data-vh-sort]")?.forEach((th) => {
    const k = th.getAttribute("data-vh-sort");
    const icon = th.querySelector(".vh-sort-icon");
    if (!icon) return;
    if (k === visitHistoryState.sortKey) {
      th.classList.add("sorted");
      icon.textContent = visitHistoryState.sortDir === 1 ? "↑" : "↓";
    } else {
      th.classList.remove("sorted");
      icon.textContent = "↕";
    }
  });
}

const refreshTokenState = { sortKey: "id", sortDir: -1, page: 1, pageSize: 10, query: "", userFilter: "" };

function buildRefreshTokenRows(tokens) {
  const arr = Array.isArray(tokens) ? tokens : [];
  const now = Date.now();
  return arr.map((x) => {
    const id = String(touristPick(x, "id", "Id") ?? "");
    const user = String(touristPick(x, "username", "Username") ?? "");
    const device = String(touristPick(x, "deviceId", "DeviceId") || "");
    let expiresRaw = touristPick(x, "expiresAtUtc", "ExpiresAtUtc");
    let revokedRaw = touristPick(x, "revokedAtUtc", "RevokedAtUtc");
    if (expiresRaw === undefined || expiresRaw === null) expiresRaw = touristPick(x, "expiresAtUTC", "ExpiresAtUTC");
    const expiresTs = expiresRaw ? new Date(expiresRaw).getTime() : 0;
    const revokedTs = revokedRaw ? new Date(revokedRaw).getTime() : 0;
    const status = revokedRaw ? "revoked" : (expiresTs > now ? "active" : "expired");
    return {
      id,
      user,
      device: device || user || "—",
      status,
      statusLabel: status === "active" ? "Đang hiệu lực" : (status === "revoked" ? "Đã revoke" : "Hết hạn"),
      expires: fmtDate(expiresRaw),
      revoked: revokedRaw ? fmtDate(revokedRaw) : "—",
      expiresTs: Number.isFinite(expiresTs) ? expiresTs : 0,
      revokedTs: Number.isFinite(revokedTs) ? revokedTs : 0
    };
  });
}

function ensureRefreshTokenEventsBound() {
  const root = byId("tourist-sec-tokens");
  if (!root || root.dataset.rtBound === "1") return;
  root.dataset.rtBound = "1";

  byId("rtSearch")?.addEventListener("input", (e) => {
    refreshTokenState.query = String(e.target?.value || "");
    refreshTokenState.page = 1;
    renderRefreshTokenExplorer(window.__tgRefreshTokenRows || []);
  });
  byId("rtUserFilter")?.addEventListener("change", (e) => {
    refreshTokenState.userFilter = String(e.target?.value || "");
    refreshTokenState.page = 1;
    renderRefreshTokenExplorer(window.__tgRefreshTokenRows || []);
  });
  byId("rtPageSize")?.addEventListener("change", (e) => {
    refreshTokenState.pageSize = Math.max(1, Number(e.target?.value || 10));
    refreshTokenState.page = 1;
    renderRefreshTokenExplorer(window.__tgRefreshTokenRows || []);
  });
  byId("rtTable")?.querySelectorAll("th[data-rt-sort]")?.forEach((th) => {
    th.addEventListener("click", () => {
      const key = th.getAttribute("data-rt-sort");
      if (!key) return;
      if (refreshTokenState.sortKey === key) refreshTokenState.sortDir *= -1;
      else {
        refreshTokenState.sortKey = key;
        refreshTokenState.sortDir = (key === "id" || key === "expires" || key === "revoked") ? -1 : 1;
      }
      refreshTokenState.page = 1;
      renderRefreshTokenExplorer(window.__tgRefreshTokenRows || []);
    });
  });
}

function renderRefreshTokenExplorer(tokens) {
  window.__tgRefreshTokenRows = tokens;
  ensureRefreshTokenEventsBound();
  const rows = buildRefreshTokenRows(tokens);

  const users = [...new Set(rows.map((r) => r.user).filter(Boolean))].sort((a, b) => a.localeCompare(b));
  const userSelect = byId("rtUserFilter");
  if (userSelect) {
    const selected = refreshTokenState.userFilter;
    userSelect.innerHTML = `<option value="">Tất cả device</option>${users.map((u) => `<option value="${escCell(u)}">${escCell(u)}</option>`).join("")}`;
    userSelect.value = users.includes(selected) ? selected : "";
    refreshTokenState.userFilter = userSelect.value;
  }

  const q = refreshTokenState.query.trim().toLowerCase();
  let filtered = rows.filter((r) => {
    const matchQ = !q || r.id.toLowerCase().includes(q) || r.user.toLowerCase().includes(q) || r.device.toLowerCase().includes(q);
    const uf = refreshTokenState.userFilter;
    const matchU = !uf || r.user === uf || r.device === uf;
    return matchQ && matchU;
  });

  filtered.sort((a, b) => {
    const key = refreshTokenState.sortKey;
    const dir = refreshTokenState.sortDir;
    if (key === "expires") return (a.expiresTs - b.expiresTs) * dir;
    if (key === "revoked") return (a.revokedTs - b.revokedTs) * dir;
    if (key === "id") return a.id.localeCompare(b.id, undefined, { numeric: true }) * dir;
    return String(a[key] || "").localeCompare(String(b[key] || "")) * dir;
  });

  const total = filtered.length;
  const pageSize = Math.max(1, Number(refreshTokenState.pageSize || 10));
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (refreshTokenState.page > pages) refreshTokenState.page = 1;
  const start = (refreshTokenState.page - 1) * pageSize;
  const view = filtered.slice(start, start + pageSize);

  const result = byId("rtResultCount");
  if (result) result.textContent = `${total} kết quả`;

  const tbody = byId("rtTbody");
  if (tbody) {
    tbody.innerHTML = view.length
      ? view.map((r) => `
        <tr>
          <td class="vh-id">${escCell(r.id)}</td>
          <td class="vh-poi">${escCell(r.device)}</td>
          <td class="vh-occurred">${escCell(r.expires)}</td>
          <td class="vh-occurred">${escCell(r.revoked)}</td>
        </tr>`).join("")
      : `<tr><td colspan="4" class="vh-no-data">Không có dữ liệu</td></tr>`;
  }

  const pageInfo = byId("rtPageInfo");
  if (pageInfo) {
    const from = total === 0 ? 0 : start + 1;
    const to = Math.min(start + pageSize, total);
    pageInfo.textContent = `Hiển thị ${from}–${to} / ${total} dòng`;
  }

  const pageBtns = byId("rtPageBtns");
  if (pageBtns) {
    let html = `<button ${refreshTokenState.page === 1 ? "disabled" : ""} data-rt-page="${refreshTokenState.page - 1}">‹</button>`;
    for (let p = 1; p <= pages; p++) {
      if (pages <= 6 || p === 1 || p === pages || Math.abs(p - refreshTokenState.page) <= 1) {
        html += `<button class="${p === refreshTokenState.page ? "active" : ""}" data-rt-page="${p}">${p}</button>`;
      } else if (Math.abs(p - refreshTokenState.page) === 2) {
        html += `<button disabled class="vh-ellipsis">…</button>`;
      }
    }
    html += `<button ${refreshTokenState.page === pages ? "disabled" : ""} data-rt-page="${refreshTokenState.page + 1}">›</button>`;
    pageBtns.innerHTML = html;
    pageBtns.querySelectorAll("button[data-rt-page]").forEach((btn) => {
      btn.addEventListener("click", () => {
        refreshTokenState.page = Number(btn.getAttribute("data-rt-page") || 1);
        renderRefreshTokenExplorer(window.__tgRefreshTokenRows || []);
      });
    });
  }

  byId("rtTable")?.querySelectorAll("th[data-rt-sort]")?.forEach((th) => {
    const k = th.getAttribute("data-rt-sort");
    const icon = th.querySelector(".vh-sort-icon");
    if (!icon) return;
    if (k === refreshTokenState.sortKey) {
      th.classList.add("sorted");
      icon.textContent = refreshTokenState.sortDir === 1 ? "↑" : "↓";
    } else {
      th.classList.remove("sorted");
      icon.textContent = "↕";
    }
  });
}

/** GET /api/pois — render bảng + select bản dịch */
async function loadPois() {
  pois = await api("/api/pois");
  byId("statPoi").textContent = String(pois.length);
  byId("statAudio").textContent = String(pois.filter(p => (p.audioUrl || "").trim() !== "").length);
  byId("statTranslation").textContent = String(pois.filter(p =>
    (p.nameEn || p.nameJa || p.nameKo || p.nameZh || p.descEn || p.descJa || p.descKo || p.descZh)
  ).length);

  const tbody = byId("poiTable").querySelector("tbody");
  tbody.innerHTML = "";
  for (const p of pois) {
    const tr = document.createElement("tr");
    const status = (p.status || "published").toLowerCase();
    const statusLabel = status === "published" ? "Published" : status === "pending" ? "Pending" : "Rejected";
    const rejectHint = status === "rejected" && (p.rejectReason || "").trim() !== ""
      ? `\nLý do: ${p.rejectReason}`
      : "";
    const approveReject =
      normalizedRole() === "admin" && status !== "published"
        ? `<button type="button" class="secondary" data-approve="${p.id}">Duyệt</button>
           <button type="button" class="secondary danger" data-reject="${p.id}">Từ chối</button>`
        : "";
    tr.innerHTML = `
      <td>${p.id}</td>
      <td>${p.nameVi}</td>
      <td>${p.tag || "Địa Điểm Du Lịch"}</td>
      <td>${Number(p.price || 0).toLocaleString("vi-VN")}</td>
      <td>${p.priority ?? 0}</td>
      <td title="${(statusLabel + rejectHint).replaceAll('"', "'")}">${statusLabel}</td>
      <td>${p.audioUrl || ""}</td>
      <td>${p.latitude}, ${p.longitude}</td>
      <td class="poi-actions"><div class="action-btns">
        <button type="button" class="secondary" data-edit="${p.id}">Sửa</button>
        <button type="button" class="danger" data-del="${p.id}" ${normalizedRole() !== "admin" ? "disabled" : ""}>Xóa</button>
        ${approveReject}
      </div></td>`;
    tbody.appendChild(tr);
  }

  const select = byId("translationPoiSelect");
  select.innerHTML = pois.map(p => `<option value="${p.id}">${p.id} - ${p.nameVi}</option>`).join("");
}

async function loadAccounts() {
  if (normalizedRole() !== "admin") return;
  const rows = await api("/api/accounts");
  const tbody = byId("accountTable").querySelector("tbody");
  tbody.innerHTML = rows.map(a => {
    const isAdmin = a.username === "admin";
    const locked = !!a.isLocked;
    const regOk = a.registrationApproved !== false;
    const pendingOwner = a.role === "owner" && !regOk;
    let statusCell;
    if (pendingOwner) {
      statusCell = `<span class="account-pending">Chờ duyệt đăng ký</span>`;
    } else if (locked) {
      statusCell = `<span class="account-locked">Đã khóa</span>`;
    } else {
      statusCell = `<span class="account-active">Hoạt động</span>`;
    }
    const lockBtns = isAdmin || pendingOwner
      ? `<span class="hint">—</span>`
      : `<button type="button" class="secondary" data-lock-id="${a.id}" data-lock-to="1" ${locked ? "disabled" : ""}>Khóa</button>
         <button type="button" class="secondary" data-lock-id="${a.id}" data-lock-to="0" ${!locked ? "disabled" : ""}>Mở khóa</button>`;
    const approveReject = pendingOwner
      ? `<button type="button" class="btn-primary" data-approve-reg="${a.id}">Duyệt</button>
         <button type="button" class="secondary danger" data-reject-reg="${a.id}">Từ chối</button>`
      : "";
    const canDelete = !isAdmin && !pendingOwner;
    const passwordHint = String(a.passwordHint || "").trim();
    const passwordHintCell = passwordHint
      ? `<code>${escCell(passwordHint)}</code>`
      : `<span class="hint">Không thể xem</span>`;
    return `
    <tr>
      <td>${a.id}</td>
      <td>${a.username}</td>
      <td>${passwordHintCell}</td>
      <td>${a.displayName}</td>
      <td>${a.role}</td>
      <td>${statusCell}</td>
      <td class="account-actions"><div class="action-btns">
        ${approveReject}
        ${lockBtns}
        <button type="button" data-del-acc="${a.id}" ${canDelete ? "" : "disabled"}>Xóa</button>
      </div></td>
    </tr>`;
  }).join("");
}

function escCell(s) {
  return String(s ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
}

/** JSON có thể camelCase hoặc PascalCase tùy cấu hình serializer. */
function touristPick(o, camel, pascal) {
  if (!o) return undefined;
  const a = o[camel];
  if (a !== undefined && a !== null) return a;
  return o[pascal];
}

function touristArray(parent, camel, pascal) {
  if (!parent) return [];
  const a = parent[camel];
  if (Array.isArray(a)) return a;
  const b = parent[pascal];
  return Array.isArray(b) ? b : [];
}

function renderVisitHistoryTopPoisLineChart(dashboard) {
  const card = byId("vhLineCard");
  const legendRoot = byId("vhLineLegend");
  const canvas = byId("vhLineChart");
  const hint = byId("vhLineRangeHint");
  if (!card || !legendRoot || !canvas) return;
  const chartData = dashboard?.visitHistoryTopPoisChart ?? dashboard?.VisitHistoryTopPoisChart;
  const w0 = chartData?.weekStartUtc ?? chartData?.WeekStartUtc;
  const w1 = chartData?.weekEndUtc ?? chartData?.WeekEndUtc;
  if (hint && w0 && w1) {
    hint.textContent = `Top 5 POI — ${w0} → ${w1} (UTC, 7 ngày)`;
  } else if (hint) {
    hint.textContent = "Top 5 POI — 7 ngày (mặc định: 7 ngày gần nhất, UTC)";
  }
  const labels = Array.isArray(chartData?.labels) ? chartData.labels : [];
  const rawSeries = Array.isArray(chartData?.series) ? chartData.series : [];
  const series = rawSeries
    .map((s) => {
      const dailyCounts = Array.isArray(s?.dailyCounts) ? s.dailyCounts.map(n => Number(n || 0)) : [];
      const weeklyTotal = dailyCounts.reduce((sum, n) => sum + n, 0);
      return { ...s, dailyCounts, weeklyTotal };
    })
    .sort((a, b) => {
      if (b.weeklyTotal !== a.weeklyTotal) return b.weeklyTotal - a.weeklyTotal;
      return String(a?.shortName || a?.poiNameVi || "").localeCompare(String(b?.shortName || b?.poiNameVi || ""));
    })
    .slice(0, 5);
  if (!labels.length || !series.length || typeof Chart === "undefined") {
    card.classList.add("hidden");
    if (visitHistoryLineChart) {
      visitHistoryLineChart.destroy();
      visitHistoryLineChart = null;
    }
    return;
  }
  card.classList.remove("hidden");

  const palette = ["#1a4fd6", "#e67e22", "#27ae60", "#8e44ad", "#e74c3c"];
  const datasets = series.map((s, idx) => {
    const color = palette[idx % palette.length];
    const y = Array.isArray(s?.dailyCounts) ? s.dailyCounts : [];
    const key = `poi-${Number(s?.poiId || idx)}`;
    return {
      key,
      label: String(s?.shortName || s?.poiNameVi || `POI ${idx + 1}`),
      data: y,
      borderColor: color,
      backgroundColor: `${color}22`,
      pointBackgroundColor: color,
      borderWidth: 2.5,
      pointRadius: 3.5,
      pointHoverRadius: 5,
      tension: 0.35,
      fill: false,
      hidden: visitHistoryLineHidden.has(key)
    };
  });

  legendRoot.innerHTML = "";
  datasets.forEach((ds, idx) => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = `vh-line-legend-item${ds.hidden ? " hidden" : ""}`;
    item.innerHTML = `<span class="vh-line-legend-dot" style="background:${ds.borderColor}"></span>${escCell(ds.label)}`;
    item.addEventListener("click", () => {
      const key = ds.key;
      if (visitHistoryLineHidden.has(key)) visitHistoryLineHidden.delete(key);
      else visitHistoryLineHidden.add(key);
      if (visitHistoryLineChart?.data?.datasets?.[idx]) {
        visitHistoryLineChart.data.datasets[idx].hidden = visitHistoryLineHidden.has(key);
        visitHistoryLineChart.update();
      }
      item.classList.toggle("hidden", visitHistoryLineHidden.has(key));
    });
    legendRoot.appendChild(item);
  });

  if (visitHistoryLineChart) {
    visitHistoryLineChart.destroy();
    visitHistoryLineChart = null;
  }
  visitHistoryLineChart = new Chart(canvas.getContext("2d"), {
    type: "line",
    data: { labels, datasets: datasets.map(({ key, ...rest }) => rest) },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: "index", intersect: false },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (ctx) => ` ${ctx.dataset.label}: ${ctx.parsed.y} lượt`
          }
        }
      },
      scales: {
        x: {
          grid: { color: "rgba(226,221,214,0.35)" },
          ticks: { color: "#a09d98", font: { size: 12 } }
        },
        y: {
          beginAtZero: true,
          grid: { color: "#e2ddd6" },
          ticks: {
            color: "#a09d98",
            font: { size: 11 },
            precision: 0,
            callback: (v) => `${v} lượt`
          }
        }
      }
    }
  });
}

function getUtcMondayMs(d = new Date()) {
  const x = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
  const day = (x.getUTCDay() + 6) % 7;
  x.setUTCDate(x.getUTCDate() - day);
  x.setUTCHours(0, 0, 0, 0);
  return x.getTime();
}

function ymdUtcFromMondayMs(ms) {
  const x = new Date(ms);
  const y = x.getUTCFullYear();
  const mo = x.getUTCMonth() + 1;
  const d = x.getUTCDate();
  return `${y}-${String(mo).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
}

function utcMidnightMsFromYmd(ymd) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(ymd || "").trim());
  if (!m) return getUtcMondayMs();
  return Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

function mondayUtcFromYW(year, week) {
  const jan4 = Date.UTC(year, 0, 4);
  const dow = (new Date(jan4).getUTCDay() + 6) % 7;
  const monW1 = jan4 - dow * 86400000;
  return monW1 + (week - 1) * 7 * 86400000;
}

function touristWeekInputFromMondayMs(mondayMs) {
  const thuMs = mondayMs + 3 * 86400000;
  const yGuess = new Date(thuMs).getUTCFullYear();
  for (const tryY of [yGuess, yGuess - 1, yGuess + 1]) {
    for (let w = 1; w <= 53; w++) {
      if (mondayUtcFromYW(tryY, w) === mondayMs) {
        return `${tryY}-W${String(w).padStart(2, "0")}`;
      }
    }
  }
  return `${yGuess}-W01`;
}

function touristMondayMsFromWeekInput(val) {
  const m = /^(\d{4})-W(\d{2})$/.exec(String(val || "").trim());
  if (!m) return getUtcMondayMs();
  return mondayUtcFromYW(Number(m[1]), Number(m[2]));
}

function touristWeekVisitCountsForRange(historyRows, mondayUtcMs) {
  const counts = [0, 0, 0, 0, 0, 0, 0];
  const start = mondayUtcMs;
  const end = mondayUtcMs + 7 * 86400000;
  const arr = Array.isArray(historyRows) ? historyRows : [];
  for (const x of arr) {
    const raw = touristPick(x, "occurredAtUtc", "OccurredAtUtc");
    if (!raw) continue;
    const t = new Date(raw).getTime();
    if (!Number.isFinite(t) || t < start || t >= end) continue;
    const idx = Math.floor((t - start) / 86400000);
    if (idx >= 0 && idx < 7) counts[idx] += 1;
  }
  return counts;
}

function touristWeekVisitAxisLabels(mondayMs) {
  return TOURIST_WEEK_VISIT_DAYS.map((label, i) => {
    const d = new Date(mondayMs + i * 86400000);
    return `${label} ${d.getUTCDate()}/${d.getUTCMonth() + 1}`;
  });
}

function touristWeekVisitTitleLine(mondayMs) {
  const wk = touristWeekInputFromMondayMs(mondayMs);
  const wNum = wk.split("-W")[1];
  const y = wk.split("-W")[0];
  const d0 = new Date(mondayMs);
  const d6 = new Date(mondayMs + 6 * 86400000);
  const first = `${d0.getUTCDate()}/${d0.getUTCMonth() + 1}`;
  const last = `${d6.getUTCDate()}/${d6.getUTCMonth() + 1}`;
  return `Tuần ${wNum} · ${first} – ${last}/${y}`;
}

function ensureTouristWeekVisitsEventsBound() {
  const root = byId("touristDashboardExtra");
  if (!root || root.dataset.twvBound === "1") return;
  root.dataset.twvBound = "1";
  root.addEventListener("click", (e) => {
    if (e.target.closest("#twvPrev")) {
      const ms = (touristWeekVisitsMondayMs ?? getUtcMondayMs()) - 7 * 86400000;
      touristWeekChartMondayParam = ymdUtcFromMondayMs(ms);
      loadTouristOverview().catch((err) => alert(err?.message || String(err)));
    } else if (e.target.closest("#twvNext")) {
      const ms = (touristWeekVisitsMondayMs ?? getUtcMondayMs()) + 7 * 86400000;
      touristWeekChartMondayParam = ymdUtcFromMondayMs(ms);
      loadTouristOverview().catch((err) => alert(err?.message || String(err)));
    }
  });
  root.addEventListener("change", (e) => {
    if (e.target?.id !== "twvWeekPick") return;
    const v = e.target.value;
    if (!v) return;
    touristWeekChartMondayParam = ymdUtcFromMondayMs(touristMondayMsFromWeekInput(v));
    loadTouristOverview().catch((err) => alert(err?.message || String(err)));
  });
}

function renderTouristWeekVisitsChart(historyRows, mondayMs, serverWeek) {
  if (typeof Chart === "undefined") return;
  const canvas = byId("twvCanvas");
  const labelEl = byId("twvLabel");
  const weekPick = byId("twvWeekPick");
  if (!canvas || !labelEl || !weekPick) return;

  const sw = serverWeek ?? null;
  let curMonday = Number(mondayMs);
  if (!Number.isFinite(curMonday)) curMonday = getUtcMondayMs();

  let curr;
  let prev;
  if (
    sw &&
    Array.isArray(sw.currentWeek) &&
    sw.currentWeek.length === 7 &&
    Array.isArray(sw.previousWeek) &&
    sw.previousWeek.length === 7
  ) {
    curr = sw.currentWeek.map((n) => Number(n) || 0);
    prev = sw.previousWeek.map((n) => Number(n) || 0);
    const wmu = sw.weekMondayUtc ?? sw.WeekMondayUtc;
    if (wmu) {
      curMonday = utcMidnightMsFromYmd(wmu);
      touristWeekVisitsMondayMs = curMonday;
    }
  } else {
    touristWeekVisitsMondayMs = curMonday;
    const prevMonday = curMonday - 7 * 86400000;
    curr = touristWeekVisitCountsForRange(historyRows, curMonday);
    prev = touristWeekVisitCountsForRange(historyRows, prevMonday);
  }

  const labels = touristWeekVisitAxisLabels(curMonday);

  labelEl.textContent = touristWeekVisitTitleLine(curMonday);
  try {
    weekPick.value = touristWeekInputFromMondayMs(curMonday);
  } catch { /* ignore */ }

  if (touristWeekVisitsChart) {
    touristWeekVisitsChart.destroy();
    touristWeekVisitsChart = null;
  }

  touristWeekVisitsChart = new Chart(canvas.getContext("2d"), {
    type: "line",
    data: {
      labels,
      datasets: [
        {
          label: "Tuần này",
          data: curr,
          borderColor: "#185FA5",
          backgroundColor: "rgba(24,95,165,0.08)",
          borderWidth: 2,
          pointBackgroundColor: "#185FA5",
          pointRadius: 4,
          pointHoverRadius: 6,
          fill: true,
          tension: 0.35
        },
        {
          label: "Tuần trước",
          data: prev,
          borderColor: "#B4B2A9",
          backgroundColor: "transparent",
          borderWidth: 1.5,
          pointBackgroundColor: "#B4B2A9",
          pointRadius: 3,
          pointHoverRadius: 5,
          fill: false,
          tension: 0.35,
          borderDash: [4, 3]
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: "index", intersect: false },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (c) => ` ${c.dataset.label}: ${c.parsed.y} lượt`
          }
        }
      },
      scales: {
        x: {
          grid: { color: "rgba(136,135,128,0.1)" },
          ticks: { font: { size: 12 }, color: "#888", autoSkip: false }
        },
        y: {
          grid: { color: "rgba(136,135,128,0.1)" },
          ticks: { font: { size: 12 }, color: "#888" },
          beginAtZero: true
        }
      }
    }
  });
}

function touristLiveSessionsHtml(live) {
  if (!live.length) {
    return `<div class="tourist-empty-state tourist-empty-state--sessions" role="status">
      <p class="tourist-empty-state__title">Chưa có phiên gần đây</p>
      <p class="tourist-empty-state__desc">Hiển thị khi có người dùng mở app với phiên đăng nhập còn hạn và có tín hiệu hoạt động trong ~2 phút gần nhất.</p>
    </div>`;
  }
  const rows = live.map((s) => {
    const u = escCell(touristPick(s, "username", "Username") || "");
    const d = escCell(touristPick(s, "displayName", "DisplayName") || "");
    const r = escCell(touristPick(s, "route", "Route") || "—");
    const tier = escCell(touristPick(s, "tierLabel", "TierLabel") || "—");
    const m = Number(touristPick(s, "minutesAgo", "MinutesAgo") ?? 0);
    const timeLabel = m <= 0 ? "Vừa xong" : `~${m} phút trước`;
    return `<div class="tourist-live-row" role="row">
      <div class="tourist-live-cell tourist-live-cell--user" role="cell">
        <span class="tourist-live-username">${u}</span>
        ${d ? `<span class="tourist-live-display">${d}</span>` : ""}
      </div>
      <div class="tourist-live-cell" role="cell"><span class="tourist-live-tier">${tier}</span></div>
      <div class="tourist-live-cell tourist-live-cell--route" role="cell" title="${r}">${r}</div>
      <div class="tourist-live-cell tourist-live-cell--time" role="cell">${escCell(timeLabel)}</div>
    </div>`;
  }).join("");
  return `<div class="tourist-live-table" role="table" aria-label="Phiên gần đây">
    <div class="tourist-live-row tourist-live-row--head" role="row">
      <div role="columnheader">Người dùng</div>
      <div role="columnheader">Tier</div>
      <div role="columnheader">Route</div>
      <div role="columnheader">Hoạt động</div>
    </div>
    ${rows}
  </div>`;
}

function touristEmptyRow(colspan, message) {
  return `<tr><td colspan="${colspan}" class="hint">${escCell(message)}</td></tr>`;
}

function commentStatusLabel(status) {
  const s = String(status || "").toLowerCase();
  if (s === "approved") return "Đã duyệt";
  if (s === "rejected") return "Từ chối";
  if (s === "hidden") return "Đã ẩn";
  return "Chờ duyệt";
}

function commentStatusClass(status) {
  const s = String(status || "").toLowerCase();
  if (s === "approved") return "approved";
  if (s === "rejected") return "rejected";
  if (s === "hidden") return "rejected";
  return "pending";
}

function renderStars(rating) {
  const n = Number.isFinite(Number(rating)) ? Math.max(1, Math.min(5, Number(rating))) : 5;
  return "★".repeat(n) + `<span class="cm-star-off">${"★".repeat(5 - n)}</span>`;
}

function renderCommentPager(totalItems, page, pageSize) {
  const pager = byId("commentPager");
  if (!pager) return;
  const totalPages = Math.max(1, Math.ceil(Number(totalItems || 0) / Math.max(1, Number(pageSize || 1))));
  const p = Math.max(1, Math.min(totalPages, Number(page || 1)));
  const pages = [];
  const start = Math.max(1, p - 2);
  const end = Math.min(totalPages, p + 2);
  for (let i = start; i <= end; i++) pages.push(i);
  pager.innerHTML = pages.map(x =>
    `<button type="button" class="cm-page-btn ${x === p ? "active" : ""}" data-page="${x}">${x}</button>`
  ).join("");
}

function renderComments(data) {
  const stats = data?.stats || {};
  byId("commentStatTotal").textContent = String(stats.total || 0);
  byId("commentStatPending").textContent = String(stats.pending || 0);
  byId("commentStatApproved").textContent = String(stats.approved || 0);
  byId("commentStatRejected").textContent = String(stats.rejected || 0);
  const pendingBadge = byId("commentPendingBadge");
  if (pendingBadge) pendingBadge.textContent = String(stats.pending || 0);

  const items = Array.isArray(data?.items) ? data.items : [];
  const list = byId("commentList");
  if (!list) return;
  if (items.length === 0) {
    list.innerHTML = `<article class="cm-card"><p class="cm-text" style="margin-left:0">Chưa có bình luận phù hợp.</p></article>`;
  } else {
    list.innerHTML = items.map(c => {
      const status = String(c.status || "pending").toLowerCase();
      const pillClass = commentStatusClass(status);
      const label = commentStatusLabel(status);
      const initials = String(c.username || "?").trim().slice(0, 2).toUpperCase();
      const statusActions = status === "approved"
        ? `<button type="button" class="cm-btn hide" data-comment-action="hidden" data-comment-id="${c.id}">Ẩn</button>`
        : `<button type="button" class="cm-btn approve" data-comment-action="approved" data-comment-id="${c.id}">Duyệt</button>
           <button type="button" class="cm-btn reject" data-comment-action="rejected" data-comment-id="${c.id}">Từ chối</button>`;
      const rejectNote = status === "rejected" && String(c.rejectReason || "").trim()
        ? `<p class="hint" style="margin:6px 0 0 46px;color:#b91c1c">Lý do: ${escCell(c.rejectReason)}</p>`
        : "";
      const replyNote = String(c.adminReply || "").trim()
        ? `<p class="hint" style="margin:6px 0 0 46px;color:#1d4ed8">Phản hồi admin: ${escCell(c.adminReply)}</p>`
        : "";
      return `
      <article class="cm-card" data-comment-id="${c.id}">
        <div class="cm-card-top">
          <div class="cm-avatar">${escCell(initials)}</div>
          <div class="cm-card-meta">
            <div class="cm-user-row">
              <span class="cm-user">${escCell(c.username)}</span>
              <span class="cm-place">${escCell(c.poiNameVi || `POI #${c.poiId}`)}</span>
            </div>
            <div class="cm-rating-row">
              <span class="cm-stars">${renderStars(c.rating)}</span>
              <span class="cm-time">${fmtDate(c.createdAtUtc)}</span>
            </div>
          </div>
          <span class="cm-pill ${pillClass}">${label}</span>
        </div>
        <p class="cm-text">${escCell(c.content)}</p>
        ${replyNote}
        ${rejectNote}
        <div class="cm-actions">
          ${statusActions}
          <button type="button" class="cm-btn reply" data-comment-reply="${c.id}">Trả lời</button>
          <button type="button" class="cm-btn" data-comment-delete="${c.id}">Xóa</button>
        </div>
      </article>`;
    }).join("");
  }

  const totalItems = Number(data?.totalItems || 0);
  commentState.totalItems = totalItems;
  const page = Number(data?.page || 1);
  const size = Number(data?.pageSize || commentState.pageSize || 8);
  const from = totalItems === 0 ? 0 : (page - 1) * size + 1;
  const to = Math.min(totalItems, page * size);
  const info = byId("commentPageInfo");
  if (info) info.textContent = `Hiển thị ${from}-${to} / ${totalItems} bình luận`;
  renderCommentPager(totalItems, page, size);
}

async function loadComments() {
  if (normalizedRole() !== "admin") return;
  const params = new URLSearchParams();
  if (commentState.status && commentState.status !== "all") params.set("status", commentState.status);
  if (commentState.search) params.set("search", commentState.search);
  params.set("page", String(commentState.page));
  params.set("pageSize", String(commentState.pageSize));
  const data = await api(`/api/comments?${params.toString()}`);
  renderComments(data);
}

function syncVisitHistoryChartYearSelect() {
  const sel = byId("vhChartYear");
  if (!sel) return;
  const y = new Date().getUTCFullYear();
  const minY = y - 5;
  const prev = sel.value;
  const opts = [];
  for (let yy = minY; yy <= y; yy++) opts.push(`<option value="${yy}">${yy}</option>`);
  sel.innerHTML = opts.join("");
  if (prev && Number(prev) >= minY && Number(prev) <= y) sel.value = prev;
  else sel.value = String(y);
}

function syncVisitHistoryChartDateInputBounds() {
  const yearSel = byId("vhChartYear");
  const dateIn = byId("vhChartWeekStart");
  if (!yearSel || !dateIn) return;
  const year = Number(yearSel.value || new Date().getUTCFullYear());
  const jan1 = `${year}-01-01`;
  const dec31 = `${year}-12-31`;
  const now = new Date();
  const uy = now.getUTCFullYear();
  const um = String(now.getUTCMonth() + 1).padStart(2, "0");
  const ud = String(now.getUTCDate()).padStart(2, "0");
  const todayUtcStr = `${uy}-${um}-${ud}`;
  /** Ngày bắt đầu tối đa: cuối năm dương lịch, và không sau hôm nay UTC (tuần gần hiện tại có thể < 7 ngày). */
  let maxStart = dec31;
  if (year === uy) maxStart = todayUtcStr < dec31 ? todayUtcStr : dec31;
  else if (year > uy) maxStart = jan1;
  if (maxStart < jan1) maxStart = jan1;
  dateIn.min = jan1;
  dateIn.max = maxStart;
  if (dateIn.value && (dateIn.value < dateIn.min || dateIn.value > dateIn.max)) {
    dateIn.value = dateIn.value < dateIn.min ? dateIn.min : dateIn.max;
  }
}

function ensureVisitHistoryChartControlsBound() {
  const root = byId("tourist-sec-visits");
  if (!root || root.dataset.vhChartBound === "1") return;
  root.dataset.vhChartBound = "1";
  syncVisitHistoryChartYearSelect();
  syncVisitHistoryChartDateInputBounds();
  byId("vhChartYear")?.addEventListener("change", () => {
    syncVisitHistoryChartDateInputBounds();
  });
  byId("vhChartApply")?.addEventListener("click", () => {
    const v = byId("vhChartWeekStart")?.value?.trim();
    if (!v) {
      alert("Chọn ngày bắt đầu (tối đa 7 ngày; gần hôm nay có thể ít hơn, theo UTC).");
      return;
    }
    visitHistoryChartState.customWeekStart = v;
    loadTouristOverview().catch((err) => alert(err?.message || String(err)));
  });
  byId("vhChartReset")?.addEventListener("click", () => {
    visitHistoryChartState.customWeekStart = null;
    const d = byId("vhChartWeekStart");
    if (d) d.value = "";
    loadTouristOverview().catch((err) => alert(err?.message || String(err)));
  });
}

async function loadTouristOverview() {
  if (normalizedRole() !== "admin") return;
  const params = new URLSearchParams();
  if (visitHistoryChartState.customWeekStart)
    params.set("visitHistoryChartWeekStart", visitHistoryChartState.customWeekStart);
  if (touristWeekChartMondayParam)
    params.set("touristWeekChartMonday", touristWeekChartMondayParam);
  const overviewUrl = params.toString() ? `/api/tourists/overview?${params.toString()}` : "/api/tourists/overview";
  const [oRes] = await Promise.allSettled([
    api(overviewUrl)
  ]);
  if (oRes.status === "rejected") throw oRes.reason;
  touristOverview = oRes.value;

  const hisArr = touristArray(touristOverview, "visitHistory", "VisitHistory");
  window.__tgVisitHistoryForWeekChart = hisArr;
  const dash = touristOverview.dashboard ?? touristOverview.Dashboard ?? {};
  const statsLine = byId("touristLoginStatsLine");
  if (statsLine) {
    const sessions = Number(touristPick(dash, "activeLoginSessions", "ActiveLoginSessions") ?? 0).toLocaleString("vi-VN");
    const accounts = Number(touristPick(dash, "activeLoginAccounts", "ActiveLoginAccounts") ?? 0).toLocaleString("vi-VN");
    statsLine.textContent =
      `Phiên đăng nhập còn hiệu lực (mỗi phiên ~ một máy đăng nhập): ${sessions} · Lượt truy cập đang có ít nhất một phiên: ${accounts}`;
  }

  const dashExtra = byId("touristDashboardExtra");
  if (dashExtra) {
    if (touristWeekVisitsChart) {
      try {
        touristWeekVisitsChart.destroy();
      } catch { /* ignore */ }
      touristWeekVisitsChart = null;
    }
    const online = Number(touristPick(dash, "onlineCount", "OnlineCount") ?? 0).toLocaleString("vi-VN");
    const totalAcc = Number(touristPick(dash, "totalAccounts", "TotalAccounts") ?? 0).toLocaleString("vi-VN");
    const activated = Number(touristPick(dash, "activatedCount", "ActivatedCount") ?? 0).toLocaleString("vi-VN");
    const sessToday = Number(touristPick(dash, "sessionsToday", "SessionsToday") ?? 0).toLocaleString("vi-VN");
    const actSess = Number(touristPick(dash, "activeLoginSessions", "ActiveLoginSessions") ?? 0).toLocaleString("vi-VN");
    const actAcc = Number(touristPick(dash, "activeLoginAccounts", "ActiveLoginAccounts") ?? 0).toLocaleString("vi-VN");
    let live = dash.liveSessions ?? dash.LiveSessions;
    if (!Array.isArray(live)) live = [];
    const pill = (num, label, modClass = "") =>
      `<article class="tourist-dash-pill ${modClass}"><p class="tourist-dash-num">${escCell(num)}</p><p class="tourist-dash-label">${escCell(label)}</p></article>`;
    dashExtra.innerHTML = `
      <div class="tourist-dash-kpis" aria-label="Chỉ số nhanh">
        ${pill(online, "Trực tuyến (~2′)", "tourist-dash-pill--tone-sky")}
        ${pill(totalAcc, "Lượt truy cập", "tourist-dash-pill--tone-slate")}
        ${pill(activated, "Đã kích hoạt / Premium", "tourist-dash-pill--tone-emerald")}
        ${pill(sessToday, "Visit history (hôm nay)", "tourist-dash-pill--tone-amber")}
      </div>
      <section class="tourist-dash-sessions" aria-labelledby="tourist-live-heading">
        <div class="tourist-dash-sessions__head">
          <h4 id="tourist-live-heading" class="tourist-live-title">Phiên gần đây</h4>
          <span class="tourist-dash-sessions__meta">Tối đa 24 · ${live.length.toLocaleString("vi-VN")} mục</span>
        </div>
        ${touristLiveSessionsHtml(live)}
      </section>
      <section class="tourist-week-visits-card" id="touristWeekVisitsCard" aria-labelledby="tourist-week-visits-heading">
        <h4 id="tourist-week-visits-heading" class="tourist-week-visits-title">Biểu đồ lượt truy cập tuần</h4>
        <p class="hint tourist-week-visits-sub">Tổng lượt (VisitHistory + log QR) theo ngày lịch UTC; server gom đủ dữ liệu DB cho tuần chọn. Bảng Visit history bên dưới vẫn chỉ hiện tối đa 300 dòng gần nhất.</p>
        <div class="tourist-week-visits-nav">
          <button type="button" class="secondary tourist-week-visits-nav-btn" id="twvPrev" aria-label="Tuần trước">←</button>
          <div class="tourist-week-visits-nav-center">
            <span class="tourist-week-visits-label" id="twvLabel"></span>
            <input type="week" id="twvWeekPick" class="tourist-week-visits-week-input">
          </div>
          <button type="button" class="secondary tourist-week-visits-nav-btn" id="twvNext" aria-label="Tuần sau">→</button>
        </div>
        <div class="tourist-week-visits-chart-wrap">
          <canvas id="twvCanvas" height="260"></canvas>
        </div>
        <div class="tourist-week-visits-legend">
          <span><span class="tourist-week-visits-leg-dot" style="background:#185FA5"></span>Tuần này</span>
          <span><span class="tourist-week-visits-leg-dot" style="background:#B4B2A9"></span>Tuần trước</span>
        </div>
      </section>
    `;
    ensureTouristWeekVisitsEventsBound();
    const weekVis = dash.weekVisitChart ?? dash.WeekVisitChart;
    const wmuChart = weekVis?.weekMondayUtc ?? weekVis?.WeekMondayUtc;
    if (wmuChart) {
      touristWeekVisitsMondayMs = utcMidnightMsFromYmd(wmuChart);
      touristWeekChartMondayParam = String(wmuChart);
    } else if (touristWeekVisitsMondayMs == null) {
      touristWeekVisitsMondayMs = getUtcMondayMs();
    }
    renderTouristWeekVisitsChart(hisArr, touristWeekVisitsMondayMs, weekVis);
  }
  const chartMeta = dash?.visitHistoryTopPoisChart ?? dash?.VisitHistoryTopPoisChart;
  const chartYear = chartMeta?.year ?? chartMeta?.Year;
  ensureVisitHistoryChartControlsBound();
  syncVisitHistoryChartYearSelect();
  if (chartYear != null && chartYear !== "") {
    const ys = byId("vhChartYear");
    if (ys && [...ys.options].some((o) => o.value === String(chartYear))) ys.value = String(chartYear);
  }
  syncVisitHistoryChartDateInputBounds();
  const wk = byId("vhChartWeekStart");
  if (wk && visitHistoryChartState.customWeekStart) wk.value = visitHistoryChartState.customWeekStart;
  renderVisitHistoryTopPoisLineChart(dash);

  const usersArr = touristArray(touristOverview, "users", "Users");
  const tokArr = touristArray(touristOverview, "refreshTokens", "RefreshTokens");

  const setMeta = (id, text) => {
    const el = byId(id);
    if (el) el.textContent = text;
  };
  setMeta("touristMetaUsers", usersArr.length ? `(hiển thị ${usersArr.length})` : "(0)");
  setMeta("touristMetaTokens", `(đang hiển thị ${tokArr.length} / tối đa 500 phiên)`);
  setMeta("touristMetaVisits", `(hiển thị ${hisArr.length} / tối đa 300)`);

  const userBody = byId("touristUserTbody") || byId("touristUserTable")?.querySelector("tbody");
  if (userBody) {
    userBody.innerHTML = usersArr.length
      ? usersArr.map((x) => {
          const id = touristPick(x, "id", "Id");
          const tier = String(touristPick(x, "accountTier", "AccountTier") || "").toLowerCase();
          return `
      <tr>
        <td>${id}</td>
        <td>${escCell(touristPick(x, "username", "Username"))}</td>
        <td>${escCell(touristPick(x, "displayName", "DisplayName"))}</td>
        <td>${Number(touristPick(x, "visitCount", "VisitCount") ?? 0).toLocaleString("vi-VN")}</td>
        <td>${Number(touristPick(x, "activeSessionCount", "ActiveSessionCount") ?? 0).toLocaleString("vi-VN")}</td>
        <td>
          <select class="tourist-tier-select" data-tier-user-id="${id}">
            <option value="free" ${tier === "free" ? "selected" : ""}>free</option>
            <option value="premium" ${tier === "premium" ? "selected" : ""}>premium</option>
          </select>
        </td>
        <td>${fmtDate(touristPick(x, "createdAtUtc", "CreatedAtUtc"))}</td>
        <td>
          <button type="button" class="secondary" data-save-tier-id="${id}">Lưu tier</button>
        </td>
      </tr>`;
        }).join("")
      : touristEmptyRow(8, "Chưa có lượt truy cập du khách.");
  }

  renderRefreshTokenExplorer(tokArr);

  renderVisitHistoryExplorer(hisArr);

}

async function loadTranslation(id) {
  const p = await api(`/api/translations/${id}`);
  byId("nameEn").value = p.nameEn || "";
  byId("nameJa").value = p.nameJa || "";
  byId("nameKo").value = p.nameKo || "";
  byId("nameZh").value = p.nameZh || "";
  byId("descEn").value = p.descEn || "";
  byId("descJa").value = p.descJa || "";
  byId("descKo").value = p.descKo || "";
  byId("descZh").value = p.descZh || "";
}

byId("authTabLogin")?.addEventListener("click", () => setAuthPanel("login"));
byId("authTabRegister")?.addEventListener("click", () => setAuthPanel("register"));

byId("registerForm")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const username = byId("regUsername").value.trim();
  const password = byId("regPassword").value;
  const password2 = byId("regPassword2").value;
  const displayName = byId("regDisplayName").value.trim();
  if (password !== password2) {
    alert("Hai lần nhập mật khẩu không khớp.");
    return;
  }
  try {
    const res = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password, displayName })
    });
    const raw = await res.text();
    if (!res.ok) {
      alert(raw || "Đăng ký thất bại.");
      return;
    }
    let msg = "Đăng ký thành công. Vui lòng đăng nhập.";
    try {
      const j = JSON.parse(raw);
      if (j.message) msg = j.message;
    } catch { /* plain text */ }
    alert(msg);
    byId("username").value = username;
    byId("password").value = "";
    byId("registerForm").reset();
    setAuthPanel("login");
  } catch {
    alert("Đăng ký thất bại — kiểm tra kết nối tới server.");
  }
});

byId("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const username = byId("username").value.trim();
  const password = byId("password").value;
  try {
    const res = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password })
    });
    if (res.status === 403) {
      let msg = "Tài khoản đã bị khóa. Liên hệ quản trị viên.";
      try {
        const j = await res.json();
        if (j.message) msg = j.message;
      } catch { /* ignore */ }
      alert(msg);
      return;
    }
    if (!res.ok) {
      let extra = "";
      try {
        const j = await res.json();
        if (j.message) extra = "\n\n" + j.message;
      } catch { /* ignore */ }
      alert("Đăng nhập thất bại." + extra);
      return;
    }
    const result = await res.json();
    token = result.token;
    const rawRole = result.role ?? result.Role ?? result.userRole ?? "";
    currentRole = String(rawRole).trim().toLowerCase();
    try {
      sessionStorage.setItem("tg_admin_token", token);
      sessionStorage.setItem("tg_admin_role", currentRole);
      sessionStorage.setItem("tg_admin_displayName", result.displayName || "");
    } catch { /* ignore */ }
    const nameEl = byId("authDisplayName");
    const roleEl = byId("authRoleLabel");
    if (nameEl) nameEl.textContent = result.displayName || "";
    syncSidebarAvatar(result.displayName || "");
    if (roleEl) {
      roleEl.textContent = currentRole === "admin" ? "Quản trị viên" : "Chủ quán";
    }
    byId("loginSection").classList.add("hidden");
    byId("appSection").classList.remove("hidden");
    applyRoleChrome();
    await loadPois();
    await loadAccounts();
    await loadComments();
    await loadTouristOverview();
    if (pois.length > 0) await loadTranslation(pois[0].id);
  } catch {
    alert("Đăng nhập thất bại.");
  }
});

byId("logoutBtn").addEventListener("click", () => {
  try {
    sessionStorage.removeItem("tg_admin_token");
    sessionStorage.removeItem("tg_admin_role");
    sessionStorage.removeItem("tg_admin_displayName");
  } catch { /* ignore */ }
  window.location.reload();
});

document.querySelectorAll(".tab").forEach(btn => {
  btn.addEventListener("click", () => {
    switchTab(btn.dataset.tab);
    if (btn.dataset.tab === "audioTab" && (normalizedRole() === "owner" || normalizedRole() === "admin")) renderAudioSources();
    if (btn.dataset.tab === "commentTab" && normalizedRole() === "admin") {
      loadComments().catch((err) => alert(err?.message || String(err)));
    }
    if (btn.dataset.tab === "touristTab" && normalizedRole() === "admin") {
      loadTouristOverview().catch((err) => {
        alert(err?.message || String(err));
      });
    }
    if (btn.dataset.tab === "heatmapTab" && normalizedRole() === "admin") {
      refreshHeatmapTab()
        .then(() => {
          setTimeout(() => {
            try {
              heatmapMap?.invalidateSize();
              heatmapMap?.getContainer?.()?.focus?.({ preventScroll: true });
            } catch { /* ignore */ }
          }, 220);
        })
        .catch((err) => alert(err?.message || String(err)));
    }
  });
});

byId("heatmapRefreshBtn")?.addEventListener("click", () => {
  if (normalizedRole() !== "admin") return;
  refreshHeatmapTab()
    .then(() => {
      setTimeout(() => {
        try {
          heatmapMap?.invalidateSize();
        } catch { /* ignore */ }
      }, 220);
    })
    .catch((err) => alert(err?.message || String(err)));
});

byId("heatmapZoomInBtn")?.addEventListener("click", () => {
  heatmapRunWhenReady(() => {
    try {
      heatmapApplyExtendedZoomCapabilities(heatmapMap);
      heatmapMap.zoomIn();
    } catch { /* ignore */ }
  });
});

byId("heatmapZoomOutBtn")?.addEventListener("click", () => {
  heatmapRunWhenReady(() => {
    try {
      heatmapMap.zoomOut();
    } catch { /* ignore */ }
  });
});

byId("heatmapFitBtn")?.addEventListener("click", () => {
  heatmapRunWhenReady(() => {
    try {
      if (!heatmapLastFitLatLngs?.length) return;
      heatmapMap.fitBounds(L.latLngBounds(heatmapLastFitLatLngs), { padding: [28, 28], maxZoom: 17 });
      heatmapMap.invalidateSize();
    } catch { /* ignore */ }
  });
});

byId("audioTableWrap")?.addEventListener("click", async (e) => {
  const uploadBtn = e.target.closest(".audio-upload-btn");
  if (uploadBtn && !uploadBtn.disabled) {
    const id = Number(uploadBtn.dataset.poiId);
    const row = uploadBtn.closest("tr");
    if (!id || !row) return;
    const lang = row.querySelector(".audio-lang-select")?.value?.trim() || "";
    if (!lang) {
      alert("Vui lòng chọn ngôn ngữ trước khi upload audio.");
      return;
    }
    const input = row.querySelector(".audio-file-input");
    const file = input?.files?.[0];
    if (!file) {
      alert("Vui lòng chọn file audio trước khi upload.");
      return;
    }

    uploadBtn.disabled = true;
    try {
      const fd = new FormData();
      fd.append("file", file);
      const result = await apiMultipart("/api/upload/audio", fd);
      const audioUrl = String(result?.audioUrl || "").trim();
      if (!audioUrl) throw new Error("Server không trả về đường dẫn audio.");

      const inAudio = row.querySelector(".audio-src-input");
      if (inAudio) inAudio.value = audioUrl;
      alert(`Upload audio (${lang.toUpperCase()}) thành công: ${audioUrl}\nBấm 'Lưu' để cập nhật vào POI.`);
    } catch (err) {
      alert(`Upload audio thất bại: ${err?.message || String(err)}`);
    } finally {
      uploadBtn.disabled = false;
    }
    return;
  }

  const btn = e.target.closest(".audio-save-btn");
  if (!btn || btn.disabled) return;
  const id = Number(btn.dataset.poiId);
  const row = btn.closest("tr");
  if (!row) return;
  const audioUrl = row.querySelector(".audio-src-input")?.value?.trim() ?? "";
  const mapLink = row.querySelector(".audio-map-input")?.value?.trim() ?? "";
  const p = pois.find(x => x.id === id);
  if (!p) return;
  btn.disabled = true;
  try {
    const payload = {
      ...p,
      audioUrl,
      mapLink
    };
    await api(`/api/pois/${id}`, { method: "PUT", body: JSON.stringify(payload) });
    await loadPois();
    renderAudioSources();
    alert("Đã lưu nguồn thuyết minh / bản đồ.");
  } catch (err) {
    alert(err?.message || String(err));
  } finally {
    btn.disabled = false;
  }
});

byId("poiTable").addEventListener("click", async (e) => {
  const raw = e.target;
  const t = raw instanceof Element ? raw : raw.parentElement;
  if (!t) return;
  const editEl = t.closest("[data-edit]");
  const delEl = t.closest("[data-del]");
  const approveEl = t.closest("[data-approve]");
  const rejectEl = t.closest("[data-reject]");
  const editId = editEl?.getAttribute("data-edit");
  const delId = delEl?.getAttribute("data-del");
  const approveId = approveEl?.getAttribute("data-approve");
  const rejectId = rejectEl?.getAttribute("data-reject");
  if (editId) {
    window.location.href = `poi-create.html?id=${encodeURIComponent(editId)}`;
    return;
  }
  if (delId && confirm("Xóa POI này?")) {
    try {
      await api(`/api/pois/${delId}`, { method: "DELETE" });
      await loadPois();
    } catch (err) {
      alert(`Xóa thất bại: ${err?.message || err}`);
    }
  }
  if (approveId && confirm("Duyệt POI này (Published)?")) {
    try {
      await api(`/api/pois/${approveId}/approve`, { method: "PUT" });
      await loadPois();
    } catch (err) {
      alert(`Duyệt thất bại: ${err?.message || err}`);
    }
  }
  if (rejectId) {
    const reason = prompt("Lý do từ chối (tuỳ chọn):", "");
    if (reason === null) return;
    try {
      await api(`/api/pois/${rejectId}/reject`, {
        method: "PUT",
        body: JSON.stringify({ reason })
      });
      await loadPois();
    } catch (err) {
      alert(`Từ chối thất bại: ${err?.message || err}`);
    }
  }
});

byId("translationPoiSelect").addEventListener("change", async (e) => {
  await loadTranslation(e.target.value);
});

byId("translationForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const id = Number(byId("translationPoiSelect").value);
  await api(`/api/translations/${id}`, {
    method: "PUT",
    body: JSON.stringify({
      nameEn: byId("nameEn").value,
      nameJa: byId("nameJa").value,
      nameKo: byId("nameKo").value,
      nameZh: byId("nameZh").value,
      descEn: byId("descEn").value,
      descJa: byId("descJa").value,
      descKo: byId("descKo").value,
      descZh: byId("descZh").value
    })
  });
  alert("Đã lưu bản dịch.");
});

byId("retranslateBtn")?.addEventListener("click", async () => {
  const id = Number(byId("translationPoiSelect").value);
  if (!Number.isFinite(id) || id <= 0) {
    alert("Vui lòng chọn POI cần dịch lại.");
    return;
  }
  if (!confirm("Dịch lại toàn bộ EN/JA/KO/ZH từ nội dung tiếng Việt hiện tại?")) return;
  try {
    await api(`/api/translations/${id}/regenerate`, { method: "POST" });
    await loadTranslation(id);
    await loadPois();
    alert("Đã dịch lại xong.");
  } catch (err) {
    alert(`Dịch lại thất bại: ${err?.message || String(err)}`);
  }
});

byId("accountForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  await api("/api/accounts", {
    method: "POST",
    body: JSON.stringify({
      username: byId("accUsername").value,
      password: byId("accPassword").value,
      displayName: byId("accDisplayName").value,
      role: byId("accRole").value
    })
  });
  byId("accountForm").reset();
  await loadAccounts();
});

byId("accountTable").addEventListener("click", async (e) => {
  const raw = e.target;
  const t = raw instanceof Element ? raw : raw.parentElement;
  if (!t) return;
  const approveEl = t.closest("[data-approve-reg]");
  const approveId = approveEl?.getAttribute("data-approve-reg");
  if (approveId && !approveEl.disabled) {
    if (!confirm("Duyệt đăng ký này? Chủ quán sẽ đăng nhập được.")) return;
    try {
      await api(`/api/accounts/${approveId}/approve-registration`, { method: "PUT" });
      await loadAccounts();
    } catch (err) {
      alert(err?.message || String(err));
    }
    return;
  }
  const rejectEl = t.closest("[data-reject-reg]");
  const rejectId = rejectEl?.getAttribute("data-reject-reg");
  if (rejectId && !rejectEl.disabled) {
    if (!confirm("Từ chối và xóa đăng ký này?")) return;
    try {
      await api(`/api/accounts/${rejectId}/reject-registration`, { method: "PUT" });
      await loadAccounts();
    } catch (err) {
      alert(err?.message || String(err));
    }
    return;
  }
  const lockEl = t.closest("[data-lock-id]");
  if (lockEl && !lockEl.disabled) {
    const id = lockEl.getAttribute("data-lock-id");
    const toLocked = lockEl.getAttribute("data-lock-to") === "1";
    const msg = toLocked
      ? "Khóa tài khoản này? Họ sẽ không đăng nhập được cho đến khi mở khóa."
      : "Mở khóa tài khoản này?";
    if (!confirm(msg)) return;
    try {
      await api(`/api/accounts/${id}/lock`, {
        method: "PUT",
        body: JSON.stringify({ locked: toLocked })
      });
      await loadAccounts();
    } catch (err) {
      alert(`Thao tác thất bại: ${err?.message || err}`);
    }
    return;
  }
  const delEl = t.closest("[data-del-acc]");
  const id = delEl?.getAttribute("data-del-acc");
  if (!id || delEl?.disabled) return;
  if (confirm("Xóa tài khoản này?")) {
    try {
      await api(`/api/accounts/${id}`, { method: "DELETE" });
      await loadAccounts();
    } catch (err) {
      alert(`Xóa thất bại: ${err?.message || err}`);
    }
  }
});

byId("exportBtn").addEventListener("click", async () => {
  const data = await api("/api/export/extra_places.json");
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = "extra_places.json";
  a.click();
  URL.revokeObjectURL(url);
});

byId("refreshTouristBtn")?.addEventListener("click", async () => {
  try {
    await loadTouristOverview();
    alert("Đã làm mới dữ liệu du khách.");
  } catch (err) {
    alert(err?.message || String(err));
  }
});

byId("touristUserTable")?.addEventListener("click", async (e) => {
  const raw = e.target;
  const t = raw instanceof Element ? raw : raw.parentElement;
  if (!t) return;

  const saveBtn = t.closest("[data-save-tier-id]");
  const userId = saveBtn?.getAttribute("data-save-tier-id");
  if (!userId || saveBtn?.disabled) return;

  const select = byId("touristUserTable")
    ?.querySelector(`select[data-tier-user-id="${userId}"]`);
  const accountTier = String(select?.value || "").trim().toLowerCase();
  if (!(accountTier === "free" || accountTier === "premium")) {
    alert("Tier không hợp lệ.");
    return;
  }

  saveBtn.disabled = true;
  try {
    await api(`/api/tourists/${userId}/tier`, {
      method: "PUT",
      body: JSON.stringify({ accountTier })
    });
    await loadTouristOverview();
    alert(`Đã cập nhật tier thành '${accountTier}'.`);
  } catch (err) {
    alert(err?.message || String(err));
  } finally {
    saveBtn.disabled = false;
  }
});

byId("commentFilterTabs")?.addEventListener("click", (e) => {
  const btn = e.target.closest(".cm-filter-tab");
  if (!btn) return;
  byId("commentFilterTabs")
    ?.querySelectorAll(".cm-filter-tab")
    .forEach(x => x.classList.remove("active"));
  btn.classList.add("active");
  commentState.status = btn.dataset.status || "all";
  commentState.page = 1;
  loadComments().catch((err) => alert(err?.message || String(err)));
});

byId("commentPager")?.addEventListener("click", (e) => {
  const btn = e.target.closest(".cm-page-btn");
  if (!btn) return;
  const p = Number(btn.dataset.page || 0);
  if (!Number.isFinite(p) || p <= 0) return;
  commentState.page = p;
  loadComments().catch((err) => alert(err?.message || String(err)));
});

byId("commentSearchInput")?.addEventListener("input", (e) => {
  const val = String(e.target?.value || "").trim();
  if (commentSearchTimer) clearTimeout(commentSearchTimer);
  commentSearchTimer = setTimeout(() => {
    commentState.search = val;
    commentState.page = 1;
    loadComments().catch((err) => alert(err?.message || String(err)));
  }, 280);
});

byId("commentList")?.addEventListener("click", async (e) => {
  const actionBtn = e.target.closest("[data-comment-action]");
  const replyBtn = e.target.closest("[data-comment-reply]");
  const delBtn = e.target.closest("[data-comment-delete]");

  if (actionBtn) {
    const id = Number(actionBtn.dataset.commentId || 0);
    const toStatus = String(actionBtn.dataset.commentAction || "").trim();
    if (!id || !toStatus) return;
    let reason = "";
    if (toStatus === "rejected") {
      const r = prompt("Lý do từ chối:", "");
      if (r === null) return;
      reason = r.trim();
    }
    try {
      await api(`/api/comments/${id}/status`, {
        method: "PUT",
        body: JSON.stringify({ status: toStatus, reason })
      });
      await loadComments();
    } catch (err) {
      alert(err?.message || String(err));
    }
    return;
  }

  if (replyBtn) {
    const id = Number(replyBtn.dataset.commentReply || 0);
    if (!id) return;
    const reply = prompt("Nhập phản hồi admin:", "");
    if (reply === null) return;
    try {
      await api(`/api/comments/${id}/reply`, {
        method: "PUT",
        body: JSON.stringify({ reply: reply.trim() })
      });
      await loadComments();
    } catch (err) {
      alert(err?.message || String(err));
    }
    return;
  }

  if (delBtn) {
    const id = Number(delBtn.dataset.commentDelete || 0);
    if (!id) return;
    if (!confirm("Xóa bình luận này?")) return;
    try {
      await api(`/api/comments/${id}`, { method: "DELETE" });
      const totalPages = Math.max(1, Math.ceil(Math.max(0, (commentState.totalItems || 0) - 1) / commentState.pageSize));
      if (commentState.page > totalPages) commentState.page = totalPages;
      await loadComments();
    } catch (err) {
      alert(err?.message || String(err));
    }
  }
});

/** Khôi phục phiên khi F5 / quay lại từ trang tạo POI (token trong sessionStorage). */
(function tryAutoLoginFromStorage() {
  try {
    const t = sessionStorage.getItem("tg_admin_token");
    const r = sessionStorage.getItem("tg_admin_role");
    if (!t || !r) return;
    const login = byId("loginSection");
    const app = byId("appSection");
    if (!login || !app) return;
    if (!app.classList.contains("hidden")) return;
    token = t;
    currentRole = String(r).trim().toLowerCase();
    const nameEl = byId("authDisplayName");
    const roleEl = byId("authRoleLabel");
    const displayName = sessionStorage.getItem("tg_admin_displayName") || "";
    if (nameEl) nameEl.textContent = displayName;
    syncSidebarAvatar(displayName);
    if (roleEl) roleEl.textContent = currentRole === "admin" ? "Quản trị viên" : "Chủ quán";
    login.classList.add("hidden");
    app.classList.remove("hidden");
    applyRoleChrome();
    loadPois()
      .then(() => loadAccounts())
      .then(() => loadComments())
      .then(() => loadTouristOverview())
      .then(async () => {
        if (pois.length > 0) await loadTranslation(pois[0].id);
      })
      .catch(() => {
        sessionStorage.removeItem("tg_admin_token");
        sessionStorage.removeItem("tg_admin_role");
        sessionStorage.removeItem("tg_admin_displayName");
        window.location.reload();
      });
  } catch { /* ignore */ }
})();
