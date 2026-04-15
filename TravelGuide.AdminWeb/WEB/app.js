/**
 * Admin SPA: gọi Minimal API (/api/*), Bearer token, bảng POI, form lưu, duyệt/từ chối, dịch, tài khoản.
 * Dữ liệu POI khớp PoiDto server (camelCase JSON).
 */
let token = "";
let currentRole = "";
let pois = [];
let touristOverview = null;
let qrBackfillRunning = false;
let qrBackfillAttempted = false;
let commentSearchTimer = null;
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
  touristTab: "Dữ liệu du khách"
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
      sub.textContent = "Tài khoản và hoạt động du khách từ ứng dụng.";
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
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString("vi-VN");
};

/** GET /api/pois — render bảng + select bản dịch */
async function loadPois() {
  pois = await api("/api/pois");
  if (!qrBackfillAttempted) {
    qrBackfillAttempted = true;
    const missingQrIds = pois
      .filter(p => !String(p.qrImagePath || "").trim())
      .map(p => Number(p.id))
      .filter(id => Number.isFinite(id) && id > 0);
    if (missingQrIds.length > 0 && !qrBackfillRunning) {
      qrBackfillRunning = true;
      let hasAnyUpdated = false;
      for (const id of missingQrIds) {
        try {
          const res = await api(`/api/pois/${id}/qrcode`, { method: "POST" });
          if (String(res?.qrImagePath || "").trim()) hasAnyUpdated = true;
        } catch {
          // Bỏ qua từng POI lỗi để vẫn xử lý các POI còn lại.
        }
      }
      qrBackfillRunning = false;
      if (hasAnyUpdated) {
        pois = await api("/api/pois");
      }
    }
  }
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
    const qrRaw = (p.qrImagePath || "").trim();
    let qrCell = "—";
    if (qrRaw) {
      const showImg = /^https?:\/\//i.test(qrRaw) || qrRaw.startsWith("/");
      qrCell = showImg
        ? `<img class="poi-qr-thumb" src="${qrRaw.replace(/"/g, "&quot;")}" alt="" loading="lazy" />`
        : `<span class="poi-qr-path">${qrRaw.replace(/</g, "&lt;")}</span>`;
    }
    tr.innerHTML = `
      <td>${p.id}</td>
      <td>${p.nameVi}</td>
      <td>${p.tag || "Địa Điểm Du Lịch"}</td>
      <td>${Number(p.price || 0).toLocaleString("vi-VN")}</td>
      <td>${p.priority ?? 0}</td>
      <td title="${(statusLabel + rejectHint).replaceAll('"', "'")}">${statusLabel}</td>
      <td class="poi-qr-cell">${qrCell}</td>
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

async function loadTouristOverview() {
  if (normalizedRole() !== "admin") return;
  const [oRes, sRes] = await Promise.allSettled([
    api("/api/tourists/overview"),
    api("/api/tourists/poi-scan-dashboard")
  ]);
  if (oRes.status === "rejected") throw oRes.reason;
  touristOverview = oRes.value;
  const scanDash = sRes.status === "fulfilled"
    ? sRes.value
    : { logs: [], revenueByPoi: [], grandTotalVnd: 0, totalScans: 0 };

  const userBody = byId("touristUserTable")?.querySelector("tbody");
  if (userBody) {
    userBody.innerHTML = (touristOverview.users || []).map(x => `
      <tr>
        <td>${x.id}</td>
        <td>${x.username}</td>
        <td>${x.displayName}</td>
        <td>
          <select class="tourist-tier-select" data-tier-user-id="${x.id}">
            <option value="free" ${String(x.accountTier).toLowerCase() === "free" ? "selected" : ""}>free</option>
            <option value="premium" ${String(x.accountTier).toLowerCase() === "premium" ? "selected" : ""}>premium</option>
          </select>
        </td>
        <td>${fmtDate(x.createdAtUtc)}</td>
        <td>
          <button type="button" class="secondary" data-save-tier-id="${x.id}">Lưu tier</button>
        </td>
      </tr>
    `).join("");
  }

  const tokenBody = byId("touristTokenTable")?.querySelector("tbody");
  if (tokenBody) {
    tokenBody.innerHTML = (touristOverview.refreshTokens || []).map(x => `
      <tr><td>${x.id}</td><td>${x.username}</td><td>${x.deviceId || ""}</td><td>${fmtDate(x.expiresAtUtc)}</td><td>${fmtDate(x.revokedAtUtc)}</td></tr>
    `).join("");
  }

  const favBody = byId("touristFavoriteTable")?.querySelector("tbody");
  if (favBody) {
    favBody.innerHTML = (touristOverview.favorites || []).map(x => `
      <tr><td>${x.id}</td><td>${x.username}</td><td>${x.poiId} - ${x.poiNameVi}</td><td>${fmtDate(x.createdAtUtc)}</td></tr>
    `).join("");
  }

  const hisBody = byId("touristHistoryTable")?.querySelector("tbody");
  if (hisBody) {
    hisBody.innerHTML = (touristOverview.visitHistory || []).map(x => `
      <tr><td>${x.id}</td><td>${x.username}</td><td>${x.poiId} - ${x.poiNameVi}</td><td>${x.eventType}</td><td>${x.playbackSeconds}</td><td>${x.watchedPercent}</td><td>${fmtDate(x.occurredAtUtc)}</td></tr>
    `).join("");
  }

  const payBody = byId("touristPaymentTable")?.querySelector("tbody");
  if (payBody) {
    payBody.innerHTML = (touristOverview.payments || []).map(x => `
      <tr><td>${x.id}</td><td>${x.username}</td><td>${x.provider}</td><td>${x.providerRef}</td><td>${x.planCode}</td><td>${Number(x.amount || 0).toLocaleString("vi-VN")} ${x.currency}</td><td>${x.status}</td><td>${fmtDate(x.createdAtUtc)}</td></tr>
    `).join("");
  }

  const revSummary = byId("touristPoiScanRevenueSummary");
  if (revSummary) {
    const gt = Number(scanDash.grandTotalVnd || 0);
    const tc = Number(scanDash.totalScans || 0);
    revSummary.textContent = `Tổng doanh thu (VND, từ quét QR): ${gt.toLocaleString("vi-VN")} · Số lượt quét: ${tc.toLocaleString("vi-VN")}`;
  }
  const revBody = byId("touristPoiScanRevenueTable")?.querySelector("tbody");
  if (revBody) {
    const rows = scanDash.revenueByPoi || [];
    revBody.innerHTML = rows.length
      ? rows.map(x => `
        <tr><td>${x.poiId}</td><td>${escCell(x.poiNameVi)}</td><td>${Number(x.totalVnd || 0).toLocaleString("vi-VN")}</td><td>${x.scanCount}</td></tr>
      `).join("")
      : `<tr><td colspan="4" class="hint">Chưa có dữ liệu quét QR.</td></tr>`;
  }
  const scanBody = byId("touristPoiScanLogTable")?.querySelector("tbody");
  if (scanBody) {
    const logs = scanDash.logs || [];
    scanBody.innerHTML = logs.length
      ? logs.map(x => `
        <tr>
          <td>${x.id}</td>
          <td>${escCell(x.username)}</td>
          <td>${x.poiId}</td>
          <td>${escCell(x.poiNameVi)}</td>
          <td>${escCell(x.eventType)}</td>
          <td>${Number(x.amountVnd || 0).toLocaleString("vi-VN")}</td>
          <td title="${escCell(x.deviceId)}">${escCell(x.deviceId)}</td>
          <td title="${escCell(x.deviceModel)}">${escCell(x.deviceModel)}</td>
          <td>${escCell(x.appPlatform)}</td>
          <td>${fmtDate(x.createdAtUtc)}</td>
        </tr>
      `).join("")
      : `<tr><td colspan="10" class="hint">Chưa có lịch sử quét.</td></tr>`;
  }
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
  });
});

byId("audioTableWrap")?.addEventListener("click", async (e) => {
  const uploadBtn = e.target.closest(".audio-upload-btn");
  if (uploadBtn && !uploadBtn.disabled) {
    const id = Number(uploadBtn.dataset.poiId);
    const row = uploadBtn.closest("tr");
    if (!id || !row) return;
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
      alert(`Upload thành công: ${audioUrl}\nBấm 'Lưu' để cập nhật vào POI.`);
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
