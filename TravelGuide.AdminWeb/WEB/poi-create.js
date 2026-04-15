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
  byId("tag").value = "dia diem du lich";
  byId("qrImagePath").value = "";
  byId("poiCreateFormTitle").textContent = "Tạo địa điểm mới";
  byId("poiCreateFormSubtitle").textContent =
    "Điền thông tin và bấm Lưu. Phí duy trì POI: 100.000đ/tháng (thu ngoài app).";
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
  byId("tag").value = p.tag || "dia diem du lich";
  byId("qrImagePath").value = p.qrImagePath || "";
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
  const editingId = Number(byId("poiId").value || 0);
  let existing = null;
  if (editingId > 0) {
    try {
      existing = await api(`/api/pois/${editingId}`);
    } catch {
      existing = null;
    }
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
    tag: byId("tag").value || "dia diem du lich",
    qrImagePath: byId("qrImagePath").value || "",
    mapLink: byId("mapLink").value || "",
    imagePath: byId("imagePath").value,
    audioUrl: byId("audioUrl").value
  };
  const id = payload.id;
  try {
    if (id > 0) {
      await api(`/api/pois/${id}`, { method: "PUT", body: JSON.stringify(payload) });
      alert("Đã cập nhật POI.");
      window.location.href = "index.html";
    } else {
      const created = await api("/api/pois", { method: "POST", body: JSON.stringify(payload) });
      const createdId = Number(created?.id || 0);
      if (createdId > 0 && !String(payload.qrImagePath || "").trim()) {
        try {
          await api(`/api/pois/${createdId}/qrcode`, { method: "POST" });
        } catch { /* ignore */ }
      }
      alert("Đã tạo POI.");
      window.location.href = "index.html";
    }
  } catch (err) {
    alert(`Lưu thất bại: ${err?.message || err}`);
  }
});

main();
