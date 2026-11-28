const references = document.querySelector("#retail-price-references");
const productName = document.querySelector("#Name");
const price = document.querySelector("#Price");

references?.querySelectorAll("button[data-price]").forEach(button => {
    button.addEventListener("click", () => {
        if (!productName.value.trim()) productName.value = button.dataset.productName;
        price.value = button.dataset.price;
        price.focus();
    });
});
