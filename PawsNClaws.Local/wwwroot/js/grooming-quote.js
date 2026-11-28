(() => {
    const total = document.getElementById("OverrideTotal");
    const reason = document.getElementById("OverrideReason");

    function updateReason() {
        const hasCustomTotal = total.value !== "";
        reason.disabled = !hasCustomTotal;
        reason.required = hasCustomTotal;
    }

    total.addEventListener("input", updateReason);
    updateReason();
})();
