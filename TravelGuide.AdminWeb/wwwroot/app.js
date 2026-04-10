/**
 * Admin SPA: gọi Minimal API (/api/*), Bearer token, bảng POI, form lưu, duyệt/từ chối, dịch, tài khoản.
 * Dữ liệu POI khớp PoiDto server (camelCase JSON).
 */
let token = "";
let currentRole = "";
let pois = [];

const qs = (s) => document.querySelector(s);
const byId = (id) => document.getElementById(id);

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

/** Ẩn/hiện tab nội dung + trạng thái nút sidebar */
function switchTab(tabId) {
  document.querySelectorAll(".tab-content").forEach(el => el.classList.add("hidden"));
  document.querySelectorAll(".tab").forEach(el => el.classList.remove("active"));
  const panel = byId(tabId);
  if (panel) panel.classList.remove("hidden");
  const tabBtn = document.querySelector(`[data-tab='${tabId}']`);
  if (tabBtn) tabBtn.classList.add("active");
}

/** Sau đăng nhập: admin vs chủ quán — nút/tab, tiêu đề, gợi ý POI. */
function applyRoleChrome() {
  document.querySelectorAll(".admin-only").forEach(el => {
    el.classList.toggle("hidden", currentRole !== "admin");
  });
  document.querySelectorAll(".owner-only").forEach(el => {
    el.classList.toggle("hidden", currentRole !== "owner");
  });
  const brandSub = byId("brandSubtitle");
  if (brandSub) {
    brandSub.textContent = currentRole === "admin" ? "Admin Portal" : "Cổng chủ quán";
  }
  const pageSub = byId("pageSubtitle");
  if (pageSub) {
    pageSub.textContent = currentRole === "admin"
      ? "Tổng quan hệ thống phố ẩm thực Vĩnh Khánh"
      : "Quản lý POI và nguồn thuyết minh thuộc quán của bạn";
  }
  const poiHead = byId("poiTabHeading");
  if (poiHead) {
    poiHead.textContent = currentRole === "admin" ? "Quản lý POI" : "Quản lý POI của quán";
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
    const save = document.createElement("button");
    save.type = "button";
    save.className = "secondary audio-save-btn";
    save.dataset.poiId = String(p.id);
    save.textContent = "Lưu";
    tdAct.appendChild(save);
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
      currentRole === "admin" && status !== "published"
        ? `<button type="button" class="secondary" data-approve="${p.id}">Duyệt</button>
           <button type="button" class="secondary danger" data-reject="${p.id}">Từ chối</button>`
        : "";
    tr.innerHTML = `
      <td>${p.id}</td>
      <td>${p.nameVi}</td>
      <td>${p.priority ?? 0}</td>
      <td title="${(statusLabel + rejectHint).replaceAll('"', "'")}">${statusLabel}</td>
      <td>${p.audioUrl || ""}</td>
      <td>${p.latitude}, ${p.longitude}</td>
      <td class="poi-actions"><div class="action-btns">
        <button type="button" class="secondary" data-edit="${p.id}">Sửa</button>
        <button type="button" data-del="${p.id}" ${currentRole !== "admin" ? "disabled" : ""}>Xóa</button>
        ${approveReject}
      </div></td>`;
    tbody.appendChild(tr);
  }

  const select = byId("translationPoiSelect");
  select.innerHTML = pois.map(p => `<option value="${p.id}">${p.id} - ${p.nameVi}</option>`).join("");
}

/** Đổ form POI khi bấm Sửa + cuộn lên form (form nằm phía trên bảng) */
function fillPoiForm(p) {
  byId("poiId").value = p.id;
  byId("nameVi").value = p.nameVi;
  byId("descVi").value = p.descVi;
  byId("lat").value = p.latitude;
  byId("lon").value = p.longitude;
  byId("radius").value = p.radius;
  byId("priority").value = p.priority ?? 0;
  byId("mapLink").value = p.mapLink || "";
  byId("imagePath").value = p.imagePath || "";
  byId("audioUrl").value = p.audioUrl || "";
  requestAnimationFrame(() => {
    byId("poiForm").scrollIntoView({ behavior: "smooth", block: "start" });
  });
}

/** Form POI mới (ẩn id) */
function clearPoiForm() {
  byId("poiForm").reset();
  byId("poiId").value = "";
}

async function loadAccounts() {
  if (currentRole !== "admin") return;
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
      ? `<button type="button" data-approve-reg="${a.id}">Duyệt</button>
         <button type="button" class="secondary danger" data-reject-reg="${a.id}">Từ chối</button>`
      : "";
    const canDelete = !isAdmin && !pendingOwner;
    return `
    <tr>
      <td>${a.id}</td><td>${a.username}</td><td>${a.displayName}</td><td>${a.role}</td>
      <td>${statusCell}</td>
      <td class="account-actions"><div class="action-btns">
        ${approveReject}
        ${lockBtns}
        <button type="button" data-del-acc="${a.id}" ${canDelete ? "" : "disabled"}>Xóa</button>
      </div></td>
    </tr>`;
  }).join("");
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
    currentRole = result.role;
    byId("authInfo").textContent = `${result.displayName} (${result.role})`;
    byId("loginSection").classList.add("hidden");
    byId("appSection").classList.remove("hidden");
    applyRoleChrome();
    await loadPois();
    await loadAccounts();
    if (pois.length > 0) await loadTranslation(pois[0].id);
  } catch {
    alert("Đăng nhập thất bại.");
  }
});

byId("logoutBtn").addEventListener("click", () => window.location.reload());

document.querySelectorAll(".tab").forEach(btn => {
  btn.addEventListener("click", () => {
    switchTab(btn.dataset.tab);
    if (btn.dataset.tab === "audioTab" && currentRole === "owner") renderAudioSources();
  });
});

byId("audioTableWrap")?.addEventListener("click", async (e) => {
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

byId("poiForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const editingId = Number(byId("poiId").value || 0);
  if (currentRole === "admin" && editingId <= 0) {
    alert("Admin không tạo mới POI trên web. Vui lòng chọn một POI hiện có để sửa.");
    return;
  }
  const existing = editingId > 0 ? pois.find(x => x.id === editingId) : null;
  const payload = {
    id: editingId,
    nameVi: byId("nameVi").value,
    nameEn: existing?.nameEn || "",
    nameJa: existing?.nameJa || "",
    nameKo: existing?.nameKo || "",
    nameZh: existing?.nameZh || "",
    descVi: byId("descVi").value,
    descEn: existing?.descEn || "",
    descJa: existing?.descJa || "",
    descKo: existing?.descKo || "",
    descZh: existing?.descZh || "",
    latitude: Number(byId("lat").value),
    longitude: Number(byId("lon").value),
    radius: Number(byId("radius").value),
    priority: Number(byId("priority").value || 0),
    mapLink: byId("mapLink").value || "",
    imagePath: byId("imagePath").value,
    audioUrl: byId("audioUrl").value
  };
  const id = payload.id;
  try {
    if (id > 0) {
      await api(`/api/pois/${id}`, { method: "PUT", body: JSON.stringify(payload) });
    } else {
      await api("/api/pois", { method: "POST", body: JSON.stringify(payload) });
    }
    clearPoiForm();
    await loadPois();
  } catch (err) {
    alert(`Lưu thất bại: ${err?.message || err}`);
  }
});

byId("newPoiBtn")?.addEventListener("click", clearPoiForm);

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
    const p = pois.find(x => x.id === Number(editId));
    if (p) fillPoiForm(p);
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
