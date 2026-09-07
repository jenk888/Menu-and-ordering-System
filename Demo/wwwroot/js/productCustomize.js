// Shared "customize before adding to cart" modal.
// Used by both Product/Index (grid) and Product/Details.
const ProductCustomize = (function () {
    const backdrop = document.getElementById("pcBackdrop");
    const titleEl = document.getElementById("pcTitle");
    const loadingEl = document.getElementById("pcLoading");
    const groupsEl = document.getElementById("pcGroups");
    const errorEl = document.getElementById("pcError");
    const qtyValueEl = document.getElementById("pcQtyValue");
    const qtyMinusBtn = document.getElementById("pcQtyMinus");
    const qtyPlusBtn = document.getElementById("pcQtyPlus");
    const confirmBtn = document.getElementById("pcConfirm");
    const closeBtn = document.getElementById("pcClose");

    let product = null;      // { id, name, price, stock, modifierGroups }
    let selections = {};     // groupId -> Set<optionId>
    let quantity = 1;
    let onAdded = null;      // callback(cartItemCount)

    function money(n) {
        return "RM" + n.toFixed(2);
    }

    function unitPrice() {
        if (!product) return 0;
        let extra = 0;
        product.modifierGroups.forEach(g => {
            g.options.forEach(o => {
                if (selections[g.id] && selections[g.id].has(o.id)) extra += o.extraPrice;
            });
        });
        return product.price + extra;
    }

    function requiredGroupsSatisfied() {
        if (!product) return false;
        return product.modifierGroups.every(g => {
            if (!g.isRequired) return true;
            return selections[g.id] && selections[g.id].size > 0;
        });
    }

    function refreshFooter() {
        const subtotal = unitPrice() * quantity;
        confirmBtn.textContent = `Confirm - ${money(subtotal)}`;
        confirmBtn.disabled = !requiredGroupsSatisfied();

        qtyMinusBtn.disabled = quantity <= 1;
        qtyPlusBtn.disabled = product ? quantity >= product.stock : true;
        qtyValueEl.textContent = quantity;
    }

    function renderGroups() {
        groupsEl.innerHTML = "";

        product.modifierGroups.forEach(group => {
            selections[group.id] = new Set();

            const wrap = document.createElement("div");
            wrap.className = "pc-group";

            const title = document.createElement("div");
            title.className = "pc-group-title";
            title.innerHTML = `<span>${group.name}</span>` +
                (group.isRequired ? `<span class="pc-required-badge">Required</span>` : "");
            wrap.appendChild(title);

            group.options.forEach(option => {
                const row = document.createElement("label");
                row.className = "pc-option";

                const inputType = group.selectionType === "Single" ? "radio" : "checkbox";
                const inputName = `pc-group-${group.id}`;

                row.innerHTML = `
                    <span class="pc-option-label">
                        <input type="${inputType}" name="${inputName}" data-group-id="${group.id}" data-option-id="${option.id}" />
                        <span>${option.name}</span>
                    </span>
                    <span class="pc-option-extra">${option.extraPrice > 0 ? "+" + money(option.extraPrice) : ""}</span>
                `;

                const input = row.querySelector("input");
                input.addEventListener("change", () => {
                    if (inputType === "radio") {
                        selections[group.id] = new Set([option.id]);
                    } else {
                        if (input.checked) selections[group.id].add(option.id);
                        else selections[group.id].delete(option.id);
                    }
                    refreshFooter();
                });

                wrap.appendChild(row);
            });

            groupsEl.appendChild(wrap);
        });
    }

    function showError(message) {
        errorEl.textContent = message;
        errorEl.style.display = message ? "block" : "none";
    }

    function close() {
        backdrop.classList.remove("open");
        product = null;
        selections = {};
        quantity = 1;
        onAdded = null;
    }

    function open(productId, addedCallback) {
        onAdded = addedCallback || null;
        showError("");
        groupsEl.innerHTML = "";
        loadingEl.style.display = "block";
        titleEl.textContent = "";
        quantity = 1;
        confirmBtn.disabled = true;
        confirmBtn.textContent = "Confirm";
        backdrop.classList.add("open");

        fetch(`/Product/GetModifiers/${encodeURIComponent(productId)}`)
            .then(res => {
                if (!res.ok) throw new Error("Could not load product options.");
                return res.json();
            })
            .then(data => {
                product = {
                    id: data.id,
                    name: data.name,
                    price: data.price,
                    stock: data.stock,
                    modifierGroups: (data.modifierGroups || []).map(g => ({
                        id: g.id,
                        name: g.name,
                        selectionType: g.selectionType,
                        isRequired: g.isRequired,
                        options: g.options || []
                    }))
                };

                loadingEl.style.display = "none";
                titleEl.textContent = product.name;
                renderGroups();
                refreshFooter();
            })
            .catch(err => {
                loadingEl.style.display = "none";
                showError(err.message || "Something went wrong loading this product.");
            });
    }

    qtyMinusBtn.addEventListener("click", () => {
        if (quantity > 1) {
            quantity--;
            refreshFooter();
        }
    });

    qtyPlusBtn.addEventListener("click", () => {
        if (product && quantity < product.stock) {
            quantity++;
            refreshFooter();
        }
    });

    closeBtn.addEventListener("click", close);
    backdrop.addEventListener("click", (e) => {
        if (e.target === backdrop) close();
    });

    confirmBtn.addEventListener("click", () => {
        if (!product || confirmBtn.disabled) return;

        const selectedModifierOptionIds = [];
        Object.values(selections).forEach(set => set.forEach(id => selectedModifierOptionIds.push(id)));

        confirmBtn.disabled = true;
        showError("");

        fetch("/Cart/Add", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                productId: product.id,
                quantity: quantity,
                selectedModifierOptionIds: selectedModifierOptionIds
            })
        })
            .then(async response => {
                const data = await response.json().catch(() => null);
                if (!response.ok) {
                    throw new Error(data?.message || `Request failed (${response.status})`);
                }
                return data;
            })
            .then(data => {
                const callback = onAdded;
                close();
                if (callback) callback(data.cartItemCount);
                document.dispatchEvent(new CustomEvent("cart:updated", { detail: { cartItemCount: data.cartItemCount } }));
            })
            .catch(err => {
                confirmBtn.disabled = !requiredGroupsSatisfied();
                showError(err.message || "Could not add item to cart. Please try again.");
            });
    });

    return { open };
})();
