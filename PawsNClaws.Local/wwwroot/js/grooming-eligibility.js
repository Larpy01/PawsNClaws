(() => {
    const species = document.getElementById("Species");
    const breed = document.getElementById("Breed");
    const options = document.getElementById("eligibility-breeds").content.querySelectorAll("option");
    const initialSpecies = species.value;
    const initialOptions = Array.from(breed.options, option => option.cloneNode(true));

    species.addEventListener("change", () => {
        const matches = species.value === initialSpecies
            ? initialOptions
            : [initialOptions[0], ...Array.from(options).filter(option => option.dataset.species === species.value)];
        breed.replaceChildren(...matches.map(option => option.cloneNode(true)));
        breed.value = "";
    });
})();
