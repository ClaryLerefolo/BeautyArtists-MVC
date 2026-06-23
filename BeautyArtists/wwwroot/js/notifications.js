let notificationConnection = null;

function initNotificationSystem(userId) {
    if (!userId) {
        console.log("No user ID provided, notifications disabled");
        return;
    }

    notificationConnection = new signalR.HubConnectionBuilder()
        .withUrl("/notificationHub")
        .withAutomaticReconnect()
        .build();

    notificationConnection.start()
        .then(() => console.log("✅ Notifications connected for user:", userId))
        .catch(err => console.error("SignalR error:", err));

    notificationConnection.on("ReceiveNotification", (notification) => {
        console.log("📢 New notification:", notification);
        showToastNotification(notification);
        loadNotifications();
        updateNotificationBell();
    });
    
    notificationConnection.on("NotificationRead", () => {
        updateNotificationBell();
        loadNotifications();
    });

    // Initial load
    loadNotifications();
    updateNotificationBell();

    // Refresh every 30 seconds
    setInterval(updateNotificationBell, 30000);
}

function loadNotifications() {
    fetch('/Notification/GetNotifications')
        .then(response => response.text())
        .then(html => {
            const contentDiv = document.getElementById('notificationContent');
            if (contentDiv) {
                contentDiv.innerHTML = html;
            }
            const loadingSpan = document.getElementById('loadingNotifications');
            if (loadingSpan) {
                loadingSpan.style.display = 'none';
            }
        })
        .catch(err => console.error('Error loading notifications:', err));
}

function updateNotificationBell() {
    fetch('/Notification/GetUnreadCount')
        .then(response => response.json())
        .then(data => {
            const badge = document.getElementById('notificationBadge');
            if (badge) {
                if (data.count > 0) {
                    badge.textContent = data.count;
                    badge.style.display = 'inline-block';
                } else {
                    badge.style.display = 'none';
                }
            }
        })
        .catch(err => console.error('Error fetching unread count:', err));
}

function markAsRead(id, actionUrl) {
    fetch(`/Notification/MarkAsRead?id=${id}`, { method: 'POST' })
        .then(() => {
            updateNotificationBell();
            loadNotifications();
            if (actionUrl) {
                window.location.href = actionUrl;
            }
        })
        .catch(err => console.error('Error marking as read:', err));
}

function markAllAsRead() {
    fetch('/Notification/MarkAllAsRead', { method: 'POST' })
        .then(() => {
            updateNotificationBell();
            loadNotifications();
        })
        .catch(err => console.error('Error marking all as read:', err));
}

function showToastNotification(notification) {
    let container = document.getElementById('toastContainer');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toastContainer';
        document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.className = 'live-toast';
    toast.innerHTML = `
        <div class="toast-icon">${notification.icon || '🔔'}</div>
        <div class="toast-content">
            <div class="toast-title">${notification.title}</div>
            <div class="toast-message">${notification.message}</div>
        </div>
        <button class="toast-close" onclick="this.parentElement.remove()">×</button>
    `;

    toast.addEventListener('click', (e) => {
        if (e.target.classList.contains('toast-close')) return;
        if (notification.actionUrl) {
            window.location.href = notification.actionUrl;
        }
        toast.remove();
    });

    container.appendChild(toast);
    setTimeout(() => toast.remove(), 5000);
}