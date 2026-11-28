const checkout = document.querySelector("#retail-checkout");
if (checkout) {
    const rows = [...checkout.querySelectorAll("[data-sale-line]")];
    const add = document.querySelector("#retail-add-line");
    const totalLabel = document.querySelector("#retail-total");
    const vatPercent = BigInt(checkout.dataset.vatPercent);
    const online = document.querySelector("#retail-online");
    const onlineDetails = document.querySelector("#retail-online-details");
    const reference = document.querySelector("#retail-online-reference");
    const amount = document.querySelector("#retail-tendered");
    const amountLabel = document.querySelector("#retail-tendered-label");
    const submit = document.querySelector("#retail-submit");
    let cashAmount = online.checked ? "" : amount.value;
    let visibleRows = Math.max(1, rows.findLastIndex(row =>
        row.querySelector("select").value || row.querySelector("input").value) + 1);

    function showRows() {
        rows.forEach((row, index) => row.classList.toggle("d-none", index >= visibleRows));
        add.hidden = visibleRows >= rows.length;
    }

    function updateTotal() {
        let centavos = 0n;
        for (const row of rows) {
            const product = row.querySelector("select").selectedOptions[0];
            const quantity = row.querySelector("input").value;
            if (!product.dataset.price || !/^[1-9][0-9]*$/.test(quantity)) continue;
            const [pesos, cents = ""] = product.dataset.price.split(".");
            centavos += (BigInt(pesos) * 100n + BigInt(cents.padEnd(2, "0"))) * BigInt(quantity);
        }
        const total = formatCentavos(centavos);
        checkout.elements.expectedTotal.value = total;
        if (online.checked) amount.value = total;
        const divisor = 100n + vatPercent;
        const vat = (2n * centavos * vatPercent + divisor) / (2n * divisor);
        totalLabel.textContent = vatPercent > 0n
            ? `Total: PHP ${total} (includes PHP ${formatCentavos(vat)} VAT)`
            : `Total: PHP ${total} (non-VAT)`;
    }

    function formatCentavos(cents) {
        return (cents / 100n) + "." + (cents % 100n).toString().padStart(2, "0");
    }

    add.addEventListener("click", () => {
        visibleRows++;
        showRows();
        rows[visibleRows - 1].querySelector("select").focus();
    });
    checkout.addEventListener("input", updateTotal);
    amount.addEventListener("input", () => {
        if (!online.checked) cashAmount = amount.value;
    });
    checkout.addEventListener("change", event => {
        if (event.target.name === "paymentMethod") showPaymentFields();
    });

    function showPaymentFields() {
        onlineDetails.hidden = !online.checked;
        reference.disabled = !online.checked;
        reference.required = online.checked;
        amount.readOnly = online.checked;
        amountLabel.textContent = online.checked ? "Online amount to verify (PHP)" : "Cash received (PHP)";
        submit.textContent = online.checked ? "Record online payment" : "Complete cash sale";
        if (!online.checked) amount.value = cashAmount;
        updateTotal();
    }

    totalLabel.hidden = false;
    showRows();
    showPaymentFields();
}
