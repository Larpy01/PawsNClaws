(() => {
    const species = document.getElementById("Species");
    const breed = document.getElementById("Breed");
    const choices = Array.from(species.list.options);
    const normalize = value => value.trim().toLowerCase();
    let previousSpecies = normalize(species.value);

    function updateSuggestions() {
        const selected = choices.find(option => normalize(option.value) === normalize(species.value));
        if (selected) {
            breed.setAttribute("list", selected.dataset.breeds);
        } else {
            breed.removeAttribute("list");
        }
    }

    species.addEventListener("input", updateSuggestions);
    species.addEventListener("change", () => {
        const currentSpecies = normalize(species.value);
        if (currentSpecies !== previousSpecies) {
            breed.value = "";
        }
        previousSpecies = currentSpecies;
        updateSuggestions();
    });
    updateSuggestions();
})();
