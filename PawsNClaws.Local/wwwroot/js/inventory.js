const inventoryProduct = document.getElementById("operation-product");
const inventoryBatch = document.getElementById("operation-batch");

if (inventoryProduct && inventoryBatch) {
    const filterBatches = () => {
        for (const option of inventoryBatch.options) {
            option.hidden = Boolean(option.dataset.product) && option.dataset.product !== inventoryProduct.value;
            option.disabled = option.hidden;
        }
        if (inventoryBatch.selectedOptions[0]?.disabled) {
            inventoryBatch.value = "";
        }
    };
    inventoryProduct.addEventListener("change", filterBatches);
    filterBatches();
}
