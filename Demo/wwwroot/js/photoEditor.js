// Usage (once per page, after including _PhotoEditorModal.cshtml):
//   initPhotoEditor({ fileInputId, chooseButtonId, previewListId });
function initPhotoEditor(opts) {
    const fileInput = document.getElementById(opts.fileInputId);
    const chooseBtn = document.getElementById(opts.chooseButtonId);
    const previewList = document.getElementById(opts.previewListId);

    const modal = document.getElementById("editPhotoModal");
    const cropperImg = document.getElementById("cropperImage");
    const rotateLeftBtn = document.getElementById("rotateLeftBtn");
    const rotateRightBtn = document.getElementById("rotateRightBtn");
    const flipHBtn = document.getElementById("flipHBtn");
    const flipVBtn = document.getElementById("flipVBtn");
    const cancelEditBtn = document.getElementById("cancelEditBtn");
    const applyEditBtn = document.getElementById("applyEditBtn");

    if (!fileInput || !chooseBtn || !previewList || !modal) return;

    let files = [];       // currently selected File objects (post-edit)
    let objectUrls = [];  // tracked so we can revoke them and avoid leaking memory
    let cropper = null;
    let editingIndex = -1;
    let flippedH = false;
    let flippedV = false;

    chooseBtn.addEventListener("click", () => fileInput.click());

    fileInput.addEventListener("change", () => {
        files = Array.from(fileInput.files);
        renderThumbnails();
    });

    function syncInputFiles() {
        const dt = new DataTransfer();
        files.forEach(f => dt.items.add(f));
        fileInput.files = dt.files;
    }

    function revokeObjectUrls() {
        objectUrls.forEach(url => URL.revokeObjectURL(url));
        objectUrls = [];
    }

    function renderThumbnails() {
        revokeObjectUrls();
        previewList.innerHTML = "";

        files.forEach((file, idx) => {
            const url = URL.createObjectURL(file);
            objectUrls.push(url);

            const wrapper = document.createElement("div");
            wrapper.className = "text-center";
            wrapper.innerHTML =
                '<img src="' + url + '" style="width:90px;height:90px;object-fit:cover;border-radius:6px;display:block;" />' +
                '<button type="button" class="btn btn-sm btn-outline-secondary mt-1 edit-photo-btn" data-idx="' + idx + '">Edit</button>';
            previewList.appendChild(wrapper);
        });

        previewList.querySelectorAll(".edit-photo-btn").forEach(btn => {
            btn.addEventListener("click", () => openEditor(parseInt(btn.dataset.idx, 10)));
        });
    }

    function openEditor(idx) {
        editingIndex = idx;
        flippedH = false;
        flippedV = false;

        cropperImg.src = objectUrls[idx];
        modal.style.display = "block";

        cropperImg.onload = () => {
            if (cropper) cropper.destroy();
            cropper = new Cropper(cropperImg, {
                viewMode: 1,
                autoCropArea: 1,
                background: false,
                aspectRatio: 1, // server always crops the saved photo to a 200x200 square,
                // so lock the crop box to match what will actually be saved
            });
        };
    }

    function closeEditor() {
        modal.style.display = "none";
        if (cropper) {
            cropper.destroy();
            cropper = null;
        }
        editingIndex = -1;
    }

    rotateLeftBtn.addEventListener("click", () => cropper && cropper.rotate(-90));
    rotateRightBtn.addEventListener("click", () => cropper && cropper.rotate(90));
    flipHBtn.addEventListener("click", () => {
        flippedH = !flippedH;
        cropper && cropper.scaleX(flippedH ? -1 : 1);
    });
    flipVBtn.addEventListener("click", () => {
        flippedV = !flippedV;
        cropper && cropper.scaleY(flippedV ? -1 : 1);
    });
    cancelEditBtn.addEventListener("click", closeEditor);

    applyEditBtn.addEventListener("click", () => {
        if (!cropper || editingIndex < 0) return;

        const canvas = cropper.getCroppedCanvas();
        if (!canvas) {
            closeEditor();
            return;
        }

        canvas.toBlob((blob) => {
            if (blob) {
                const originalName = files[editingIndex].name;
                files[editingIndex] = new File([blob], originalName, { type: "image/jpeg" });
                syncInputFiles();
                renderThumbnails();
            }
            closeEditor();
        }, "image/jpeg", 0.9);
    });

    // ------------------------------------------------------------------
    // Webcam capture — optional, only wired up if the page provides a
    // webcamButtonId and the matching _WebcamCaptureModal partial.
    // ------------------------------------------------------------------
    const webcamBtn = opts.webcamButtonId ? document.getElementById(opts.webcamButtonId) : null;
    const webcamModal = document.getElementById("webcamModal");
    const webcamVideo = document.getElementById("webcamVideo");
    const webcamCanvas = document.getElementById("webcamCanvas");
    const webcamError = document.getElementById("webcamError");
    const captureBtn = document.getElementById("capturePhotoBtn");
    const retakeBtn = document.getElementById("retakePhotoBtn");
    const usePhotoBtn = document.getElementById("usePhotoBtn");
    const cancelWebcamBtn = document.getElementById("cancelWebcamBtn");

    if (webcamBtn && webcamModal && webcamVideo && webcamCanvas && captureBtn && usePhotoBtn && cancelWebcamBtn) {
        let webcamStream = null;
        let capturedBlob = null;

        webcamBtn.addEventListener("click", openWebcam);
        cancelWebcamBtn.addEventListener("click", closeWebcam);
        captureBtn.addEventListener("click", capturePhoto);
        if (retakeBtn) retakeBtn.addEventListener("click", showLiveView);
        usePhotoBtn.addEventListener("click", useCaptured);

        async function openWebcam() {
            capturedBlob = null;
            showLiveView();
            if (webcamError) webcamError.style.display = "none";
            webcamModal.style.display = "block";

            try {
                webcamStream = await navigator.mediaDevices.getUserMedia({ video: true });
                webcamVideo.srcObject = webcamStream;
            } catch (err) {
                if (webcamError) {
                    webcamError.textContent = "Could not access the camera: " + err.message;
                    webcamError.style.display = "block";
                }
            }
        }

        function showLiveView() {
            capturedBlob = null;
            webcamVideo.style.display = "block";
            webcamCanvas.style.display = "none";
            captureBtn.style.display = "inline-block";
            if (retakeBtn) retakeBtn.style.display = "none";
            usePhotoBtn.style.display = "none";
        }

        function showCapturedView() {
            webcamVideo.style.display = "none";
            webcamCanvas.style.display = "block";
            captureBtn.style.display = "none";
            if (retakeBtn) retakeBtn.style.display = "inline-block";
            usePhotoBtn.style.display = "inline-block";
        }

        function stopStream() {
            if (webcamStream) {
                webcamStream.getTracks().forEach(t => t.stop());
                webcamStream = null;
            }
        }

        function closeWebcam() {
            stopStream();
            webcamModal.style.display = "none";
            capturedBlob = null;
        }

        function capturePhoto() {
            if (!webcamVideo.videoWidth) return; // stream not ready yet

            webcamCanvas.width = webcamVideo.videoWidth;
            webcamCanvas.height = webcamVideo.videoHeight;
            webcamCanvas.getContext("2d").drawImage(webcamVideo, 0, 0);

            webcamCanvas.toBlob((blob) => {
                capturedBlob = blob;
            }, "image/jpeg", 0.9);

            showCapturedView();
        }

        function useCaptured() {
            if (!capturedBlob) return;

            files.push(new File([capturedBlob], "webcam-" + Date.now() + ".jpg", { type: "image/jpeg" }));
            syncInputFiles();
            renderThumbnails();
            closeWebcam();
        }
    }
}