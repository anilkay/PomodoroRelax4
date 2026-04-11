// Notification sound player for PomodoroRelax
window.pomodoroSound = {
    play: function () {
        try {
            const audio = new Audio('/sounds/notification.wav');
            audio.volume = 0.7;
            audio.play().catch(err => console.warn('Sound play failed:', err));
        } catch (e) {
            console.warn('Sound not available:', e);
        }
    }
};
