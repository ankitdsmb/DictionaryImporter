const form = document.getElementById("generator-form");
const progressSection = document.getElementById("progress-section");
const progressMessage = document.getElementById("progress-message");
const progressBar = document.getElementById("progress-bar");
const resultSection = document.getElementById("result-section");
const resultVideo = document.getElementById("result-video");
const downloadLink = document.getElementById("download-link");
const errorMessage = document.getElementById("error-message");
const generateBtn = document.getElementById("generate-btn");

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  resetUI();

  const payload = {
    topic: document.getElementById("topic").value.trim(),
    style: document.getElementById("style").value,
    resolution: document.getElementById("resolution").value,
    film_grain: document.getElementById("film_grain").checked,
    ken_burns: document.getElementById("ken_burns").checked,
  };

  try {
    generateBtn.disabled = true;
    progressSection.classList.remove("hidden");

    const response = await fetch("/generate-video", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (!response.ok) throw new Error(`Unable to start generation (${response.status})`);

    const data = await response.json();
    await pollStatus(data.request_id);
  } catch (error) {
    showError(error.message || "Unexpected error");
  } finally {
    generateBtn.disabled = false;
  }
});

async function pollStatus(requestId) {
  while (true) {
    const response = await fetch(`/status/${requestId}`);
    if (!response.ok) throw new Error("Failed to fetch generation status");

    const status = await response.json();
    progressMessage.textContent = status.message || "Rendering";
    progressBar.style.width = `${status.progress || 0}%`;

    if (status.status === "completed") {
      resultSection.classList.remove("hidden");
      resultVideo.src = status.video_url;
      resultVideo.load();
      downloadLink.href = status.video_url;
      progressSection.classList.add("hidden");
      return;
    }

    if (status.status === "failed") {
      throw new Error(status.message || "Video generation failed");
    }

    await wait(2000);
  }
}

function resetUI() {
  errorMessage.classList.add("hidden");
  resultSection.classList.add("hidden");
  progressBar.style.width = "5%";
  progressMessage.textContent = "Preparing generation...";
}

function showError(message) {
  errorMessage.textContent = message;
  errorMessage.classList.remove("hidden");
  progressSection.classList.add("hidden");
}

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
