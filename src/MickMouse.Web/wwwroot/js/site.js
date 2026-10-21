'use strict';
// Native controls remain usable when JavaScript is unavailable.
document.querySelectorAll('form[data-confirm]').forEach(form => {
    form.addEventListener('submit', event => {
        if (!window.confirm(form.dataset.confirm)) event.preventDefault();
    });
});

document.querySelectorAll('input[type="file"][data-max-bytes]').forEach(input => {
    input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        if (file && file.size > Number(input.dataset.maxBytes)) {
            input.setCustomValidity('Choose an audio or video file smaller than 25 MiB.');
            input.reportValidity();
        } else input.setCustomValidity('');
    });
});

const player = document.querySelector('[data-episode-player]');
const trackingForm = document.querySelector('[data-play-tracker]');
if (player && trackingForm) {
    let recorded = false;
    let sending = false;
    player.addEventListener('play', async () => {
        if (recorded || sending) return;
        sending = true;
        try {
            const response = await fetch(trackingForm.action, {
                method: 'POST',
                credentials: 'same-origin',
                body: new FormData(trackingForm),
                headers: { 'Accept': 'application/json' }
            });
            if (!response.ok) throw new Error('The listen count could not be saved.');
            const data = await response.json();
            const count = document.getElementById('play-count');
            if (count && typeof data.playCount === 'number') count.textContent = data.playCount.toLocaleString();
            recorded = true;
        } catch {
            const status = document.querySelector('[data-play-status]');
            if (status) {
                status.textContent = 'Your recording is playing. The listen count could not be updated just now.';
                status.hidden = false;
            }
        } finally {
            sending = false;
        }
    });
}
