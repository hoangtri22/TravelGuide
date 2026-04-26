/**
 * Trang riêng tạo/sửa POI: cần đăng nhập (token trong sessionStorage từ index).
 */
const byId = (id) => document.getElementById(id);

const api = async (url, options = {}) => {
  const t = sessionStorage.getItem("tg_admin_token") || "";
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (t) headers.Authorization = `Bearer ${t}`;
  const res = await fetch(url, { ...options, headers });
  if (!res.ok) {
    const text = (await res.text()).trim();
    if (res.status === 401) throw new Error(text || "Phiên đăng nhập không hợp lệ.");
    if (res.status === 403) throw new Error(text || "Không có quyền.");
    throw new Error(text || `Lỗi ${res.status}`);
  }
  if (res.status === 204) return null;
  const ct = res.headers.get("content-type") || "";
  return ct.includes("application/json") ? res.json() : res.text();
};

const apiMultipart = async (url, formData, options = {}) => {
  const t = sessionStorage.getItem("tg_admin_token") || "";
  const headers = { ...(options.headers || {}) };
  if (t) headers.Authorization = `Bearer ${t}`;
  const res = await fetch(url, { ...options, method: options.method || "POST", headers, body: formData });
  if (!res.ok) {
    const text = (await res.text()).trim();
    if (res.status === 401) throw new Error(text || "Phiên đăng nhập không hợp lệ.");
    if (res.status === 403) throw new Error(text || "Không có quyền.");
    throw new Error(text || `Lỗi ${res.status}`);
  }
  const ct = res.headers.get("content-type") || "";
  return ct.includes("application/json") ? res.json() : res.text();
};

function normalizedRole() {
  return String(sessionStorage.getItem("tg_admin_role") || "").trim().toLowerCase();
}

function showFormUi() {
  byId("poiCreateGate")?.classList.add("hidden");
  byId("poiCreateFormWrap")?.classList.remove("hidden");
  byId("btnAnotherPoi")?.classList.remove("hidden");
}

function resetEmptyForm() {
  const form = byId("poiForm");
  if (!form) return;
  form.reset();
  byId("poiId").value = "";
  byId("price").value = "1000";
  byId("tag").value = "Địa Điểm Du Lịch";
  byId("poiCreateFormTitle").textContent = "Tạo địa điểm mới";
  byId("poiCreateFormSubtitle").textContent =
    "Điền thông tin và bấm Lưu. Phí duy trì POI: 100.000đ/tháng (thu ngoài app).";
}

function normalizeTagForSelect(rawTag) {
  const t = String(rawTag || "").trim().toLowerCase();
  if (!t) return "Địa Điểm Du Lịch";
  if (t === "quan an" || t === "quán ăn") return "Quán Ăn";
  if (t === "quan nuoc" || t === "quán nước") return "Quán Nước";
  if (t === "di tich lich su" || t === "di tích lịch sử") return "Di Tích Lịch Sử";
  if (t === "dia diem du lich" || t === "địa điểm du lịch") return "Địa Điểm Du Lịch";
  return rawTag;
}

function requireAuth() {
  const t = sessionStorage.getItem("tg_admin_token");
  if (!t) {
    window.location.href = "index.html";
    return false;
  }
  const r = normalizedRole();
  if (r !== "admin" && r !== "owner") {
    alert("Chỉ admin hoặc chủ quán được tạo POI.");
    window.location.href = "index.html";
    return false;
  }
  return true;
}

function parseEditId() {
  const q = new URLSearchParams(window.location.search);
  const id = Number(q.get("id") || 0);
  return Number.isFinite(id) && id > 0 ? id : 0;
}

async function loadPoiForEdit(id) {
  const p = await api(`/api/pois/${id}`);
  byId("poiId").value = String(p.id);
  byId("nameVi").value = p.nameVi || "";
  byId("descVi").value = p.descVi || "";
  byId("lat").value = p.latitude;
  byId("lon").value = p.longitude;
  byId("radius").value = p.radius;
  byId("priority").value = p.priority ?? 0;
  byId("price").value = p.price ?? 0;
  byId("tag").value = normalizeTagForSelect(p.tag);
  byId("mapLink").value = p.mapLink || "";
  byId("imagePath").value = p.imagePath || "";
  byId("audioUrl").value = p.audioUrl || "";
  byId("poiCreateFormTitle").textContent = `Sửa POI #${p.id}`;
  byId("poiCreateFormSubtitle").textContent = p.nameVi || "";
}

async function main() {
  if (!requireAuth()) return;

  const role = normalizedRole();
  const hint = byId("poiCreateRoleHint");
  if (hint) hint.textContent = role === "admin" ? "Quản trị viên" : "Chủ quán";

  const editId = parseEditId();
  if (editId > 0) {
    try {
      await loadPoiForEdit(editId);
      byId("poiCreateGate")?.classList.add("hidden");
      showFormUi();
    } catch (e) {
      alert(e?.message || String(e));
      window.location.href = "index.html";
    }
    return;
  }

  byId("btnShowPoiForm")?.addEventListener("click", () => {
    resetEmptyForm();
    showFormUi();
    byId("nameVi")?.focus();
  });

  byId("btnAnotherPoi")?.addEventListener("click", () => {
    window.location.href = "poi-create.html";
  });
}

byId("poiForm")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const submitBtn = e.submitter;
  if (submitBtn) submitBtn.disabled = true;
  const editingId = Number(byId("poiId").value || 0);
  let existing = null;
  if (editingId > 0) {
    try {
      existing = await api(`/api/pois/${editingId}`);
    } catch {
      existing = null;
    }
  }
  let resolvedAudioUrl = (byId("audioUrl").value || "").trim();
  const selectedAudio = byId("audioFile")?.files?.[0];
  if (selectedAudio) {
    const fd = new FormData();
    fd.append("file", selectedAudio);
    const uploaded = await apiMultipart("/api/upload/audio", fd);
    resolvedAudioUrl = String(uploaded?.audioUrl || "").trim();
    if (!resolvedAudioUrl) throw new Error("Upload audio không trả về đường dẫn.");
    byId("audioUrl").value = resolvedAudioUrl;
  }

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
    price: Number(byId("price").value || 0),
    tag: byId("tag").value || "Địa Điểm Du Lịch",
    mapLink: byId("mapLink").value || "",
    imagePath: byId("imagePath").value,
    audioUrl: resolvedAudioUrl
  };
  const id = payload.id;
  try {
    if (id > 0) {
      await api(`/api/pois/${id}`, { method: "PUT", body: JSON.stringify(payload) });
      alert("Đã cập nhật POI.");
      window.location.href = "index.html";
    } else {
      await api("/api/pois", { method: "POST", body: JSON.stringify(payload) });
      alert("Đã tạo POI.");
      window.location.href = "index.html";
    }
  } catch (err) {
    alert(`Lưu thất bại: ${err?.message || err}`);
  } finally {
    if (submitBtn) submitBtn.disabled = false;
  }
});

main();
