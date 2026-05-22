document.getElementById('sidebarToggle').addEventListener('click', () => {
    document.getElementById('sidebar').classList.toggle('collapsed');
    document.querySelector('.content').classList.toggle('collapsed');
});

fetch('/Artist/GetNotifications')
    .then(r => r.json())
    .then(data => {
        const menu = document.getElementById('notifMenu');
        menu.innerHTML = data.map(n => `<li><a class="dropdown-item">${n}</a></li>`).join('');
        document.getElementById('notifBadge').innerText = data.length;
    });
