let token = "";
let currentRole = "";
let pois = [];

const qs = (s) => document.querySelector(s);
const byId = (id) => document.getElementById(id);

const api = async (url, options = {}) => {
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(url, { ...options, headers });
  if (!res.ok) throw new Error(await res.text());
  if (res.status === 204) return null;
  const ct = res.headers.get("content-type") || "";
  return ct.includes("application/json") ? res.json() : res.text();
};

function switchTab(tabId) {
  document.querySelectorAll(".tab-content").forEach(el => el.classList.add("hidden"));
  document.querySelectorAll(".tab").forEach(el => el.classList.remove("active"));
  byId(tabId).classList.remove("hidden");
  qs(`[data-tab='${tabId}']`).classList.add("active");
}

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
    tr.innerHTML = `
      <td>${p.id}</td>
      <td>${p.nameVi}</td>
      <td>${p.audioUrl || ""}</td>
      <td>${p.latitude}, ${p.longitude}</td>
      <td>
        <button class="secondary" data-edit="${p.id}">Sửa</button>
        <button data-del="${p.id}" ${currentRole !== "admin" ? "disabled" : ""}>Xóa</button>
      </td>`;
    tbody.appendChild(tr);
  }

  const select = byId("translationPoiSelect");
  select.innerHTML = pois.map(p => `<option value="${p.id}">${p.id} - ${p.nameVi}</option>`).join("");
}

function fillPoiForm(p) {
  byId("poiId").value = p.id;
  byId("nameVi").value = p.nameVi;
  byId("descVi").value = p.descVi;
  byId("lat").value = p.latitude;
  byId("lon").value = p.longitude;
  byId("radius").value = p.radius;
  byId("imagePath").value = p.imagePath || "";
  byId("audioUrl").value = p.audioUrl || "";
}

function clearPoiForm() {
  byId("poiForm").reset();
  byId("poiId").value = "";
}

async function loadAccounts() {
  if (currentRole !== "admin") return;
  const rows = await api("/api/accounts");
  const tbody = byId("accountTable").querySelector("tbody");
  tbody.innerHTML = rows.map(a => `
    <tr>
      <td>${a.id}</td><td>${a.username}</td><td>${a.displayName}</td><td>${a.role}</td>
      <td><button data-del-acc="${a.id}" ${a.username === "admin" ? "disabled" : ""}>Xóa</button></td>
    </tr>`).join("");
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

byId("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  try {
    const username = byId("username").value.trim();
    const password = byId("password").value;
    const result = await api("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password })
    });
    token = result.token;
    currentRole = result.role;
    byId("authInfo").textContent = `${result.displayName} (${result.role})`;
    byId("loginSection").classList.add("hidden");
    byId("appSection").classList.remove("hidden");
    document.querySelectorAll(".admin-only").forEach(el => {
      if (currentRole !== "admin") el.classList.add("hidden");
    });
    await loadPois();
    await loadAccounts();
    if (pois.length > 0) await loadTranslation(pois[0].id);
  } catch {
    alert("Đăng nhập thất bại.");
  }
});

byId("logoutBtn").addEventListener("click", () => window.location.reload());

document.querySelectorAll(".tab").forEach(btn => {
  btn.addEventListener("click", () => switchTab(btn.dataset.tab));
});

byId("poiForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const editingId = Number(byId("poiId").value || 0);
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
    imagePath: byId("imagePath").value,
    audioUrl: byId("audioUrl").value
  };
  const id = payload.id;
  if (id > 0) {
    await api(`/api/pois/${id}`, { method: "PUT", body: JSON.stringify(payload) });
  } else {
    await api("/api/pois", { method: "POST", body: JSON.stringify(payload) });
  }
  clearPoiForm();
  await loadPois();
});

byId("newPoiBtn").addEventListener("click", clearPoiForm);

byId("poiTable").addEventListener("click", async (e) => {
  const editId = e.target.getAttribute("data-edit");
  const delId = e.target.getAttribute("data-del");
  if (editId) {
    const p = pois.find(x => x.id === Number(editId));
    if (p) fillPoiForm(p);
  }
  if (delId && confirm("Xóa POI này?")) {
    await api(`/api/pois/${delId}`, { method: "DELETE" });
    await loadPois();
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
  const id = e.target.getAttribute("data-del-acc");
  if (!id) return;
  if (confirm("Xóa tài khoản này?")) {
    await api(`/api/accounts/${id}`, { method: "DELETE" });
    await loadAccounts();
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
