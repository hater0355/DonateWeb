(() => {
    const root = document.getElementById('supportChat');
    if (!root) return;

    const launcher = document.getElementById('supportChatLauncher');
    const panel = document.getElementById('supportChatPanel');
    const closeButton = document.getElementById('supportChatClose');
    const form = document.getElementById('supportChatForm');
    const input = document.getElementById('supportChatInput');
    const messagesView = document.getElementById('supportChatMessages');
    const empty = document.getElementById('supportChatEmpty');
    const note = document.getElementById('supportChatNote');
    const visitorName = document.getElementById('supportChatVisitorName');
    const csrf = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    let conversationId = null;
    let lastMessageId = 0;
    let status = 'Open';
    let pollTimer = null;
    let started = false;

    const setNote = text => { note.textContent = text; };
    const request = async (url, options = {}) => {
        const response = await fetch(url, { ...options, credentials: 'same-origin', headers: { 'Accept': 'application/json', ...(options.headers || {}) } });
        if (!response.ok) throw new Error(response.status === 409 ? 'Cuộc trò chuyện đã đóng.' : 'Không thể kết nối hộp thư hỗ trợ.');
        return response.status === 204 ? null : response.json();
    };

    function appendMessage(message) {
        empty.hidden = true;
        const row = document.createElement('div');
        row.className = `support-chat-message ${message.isFromSupport ? 'from-support' : 'from-visitor'}`;
        const name = document.createElement('strong');
        name.textContent = message.isFromSupport ? 'Hỗ trợ viên' : message.senderName;
        const text = document.createElement('div');
        text.className = 'support-chat-message-text';
        text.textContent = message.content;
        const time = document.createElement('time');
        time.dateTime = message.createdAt;
        time.textContent = new Date(message.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
        row.append(name, text, time);
        messagesView.appendChild(row);
        lastMessageId = Math.max(lastMessageId, Number(message.id));
        messagesView.scrollTop = messagesView.scrollHeight;
    }

    async function ensureConversation() {
        if (conversationId) return;
        if (!started) {
            started = true;
            setNote('Đang mở cuộc trò chuyện…');
            const data = new URLSearchParams();
            data.set('__RequestVerificationToken', csrf);
            if (visitorName?.value.trim()) data.set('visitorName', visitorName.value.trim());
            const current = await request(root.dataset.currentUrl);
            if (current.conversationId) {
                conversationId = current.conversationId;
                status = current.status;
            } else {
                const created = await request(root.dataset.startUrl, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': csrf },
                    body: data
                });
                conversationId = created.conversationId;
                status = created.status;
            }
        }
    }

    async function refreshMessages() {
        if (!conversationId || !panel.hidden) return;
        const data = await request(`${root.dataset.messagesUrl}/${conversationId}?afterMessageId=${lastMessageId}`);
        status = data.status;
        data.messages.forEach(appendMessage);
        if (status !== 'Open') {
            input.disabled = true;
            form.querySelector('button[type="submit"]').disabled = true;
            setNote('Cuộc trò chuyện đã kết thúc. Hãy tải lại trang để gửi câu hỏi mới.');
        } else {
            setNote('Không gửi mật khẩu hoặc thông tin thẻ ngân hàng qua chat.');
        }
    }

    launcher.addEventListener('click', async () => {
        panel.hidden = false;
        launcher.setAttribute('aria-expanded', 'true');
        input.focus();
        if (!started) {
            try { await ensureConversation(); await refreshMessages(); }
            catch (error) { started = false; setNote(error.message); }
        }
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(() => refreshMessages().catch(error => setNote(error.message)), 3000);
    });

    function closePanel() {
        panel.hidden = true;
        launcher.setAttribute('aria-expanded', 'false');
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = null;
    }
    closeButton.addEventListener('click', closePanel);

    form.addEventListener('submit', async event => {
        event.preventDefault();
        const message = input.value.trim();
        if (!message) return;
        const submit = form.querySelector('button[type="submit"]');
        submit.disabled = true;
        try {
            await ensureConversation();
            const data = new URLSearchParams({ conversationId: String(conversationId), message, __RequestVerificationToken: csrf });
            await request(root.dataset.sendUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': csrf },
                body: data
            });
            input.value = '';
            await refreshMessages();
        } catch (error) {
            setNote(error.message);
        } finally {
            submit.disabled = status !== 'Open';
        }
    });

    input.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            form.requestSubmit();
        }
    });
})();
