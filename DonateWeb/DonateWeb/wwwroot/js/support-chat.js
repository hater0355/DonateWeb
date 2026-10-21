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
    const aiEnabled = root.dataset.aiEnabled === 'true';
    const defaultNote = aiEnabled
        ? 'Trợ lý AI có thể sai. Gõ “gặp hỗ trợ viên” để chuyển sang người thật; không gửi mật khẩu, OTP hoặc thông tin thẻ.'
        : 'AI chưa được cấu hình; tin nhắn vẫn được gửi đến đội hỗ trợ. Không gửi mật khẩu, OTP hoặc thông tin thẻ.';
    const csrf = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    let conversationId = null;
    let lastMessageId = 0;
    let status = 'Open';
    let pollTimer = null;
    let started = false;

    const setNote = text => { note.textContent = text; };
    const request = async (url, options = {}) => {
        const response = await fetch(url, { ...options, credentials: 'same-origin', headers: { 'Accept': 'application/json', ...(options.headers || {}) } });
        if (!response.ok) {
            const error = new Error(response.status === 404
                ? 'Phiên trò chuyện đã hết hạn.'
                : response.status === 409 ? 'Cuộc trò chuyện đã đóng.' : 'Không thể kết nối hộp thư hỗ trợ.');
            error.status = response.status;
            throw error;
        }
        return response.status === 204 ? null : response.json();
    };

    function resetExpiredConversation(error) {
        if (error?.status !== 404) return false;

        // Phiên đã được máy chủ xóa sau thời gian không hoạt động; sẵn sàng tạo phiên mới.
        conversationId = null;
        lastMessageId = 0;
        status = 'Open';
        started = false;
        messagesView.replaceChildren();
        empty.hidden = false;
        input.disabled = false;
        form.querySelector('button[type="submit"]').disabled = false;
        setNote('Phiên hỗ trợ đã hết hạn. Gửi tin nhắn mới để bắt đầu cuộc trò chuyện khác.');
        return true;
    }

    function appendMessage(message) {
        empty.hidden = true;
        const row = document.createElement('div');
        row.className = `support-chat-message ${message.isFromSupport ? 'from-support' : 'from-visitor'}${message.isAi ? ' from-ai' : ''}`;
        const name = document.createElement('strong');
        name.textContent = message.isAi ? 'Trợ lý AI DonateWeb' : message.isFromSupport ? 'Hỗ trợ viên' : message.senderName;
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
        // Chỉ tải khi cửa sổ chat đang mở. Điều kiện cũ bị đảo, khiến widget
        // bỏ qua cả tin vừa gửi lẫn câu trả lời từ Hỗ trợ viên.
        if (!conversationId || panel.hidden) return;
        let data;
        try {
            data = await request(`${root.dataset.messagesUrl}/${conversationId}?afterMessageId=${lastMessageId}`);
        } catch (error) {
            if (resetExpiredConversation(error)) return;
            throw error;
        }
        status = data.status;
        if (data.aiPaused) setNote('Hỗ trợ viên sẽ tiếp quản cuộc trò chuyện.');
        data.messages.forEach(appendMessage);
        if (status !== 'Open') {
            input.disabled = true;
            form.querySelector('button[type="submit"]').disabled = true;
            setNote('Cuộc trò chuyện đã kết thúc. Hãy tải lại trang để gửi câu hỏi mới.');
        } else {
            if (!data.aiPaused) setNote(defaultNote);
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
            const result = await request(root.dataset.sendUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': csrf },
                body: data
            });
            input.value = '';
            await refreshMessages();
            if (result.aiStatus === 'handoff') setNote('Đã chuyển cuộc trò chuyện cho Hỗ trợ viên.');
            else if (result.aiStatus === 'outOfScope') setNote('Trợ lý AI chỉ hỗ trợ các vấn đề liên quan đến DonateWeb.');
            else if (result.aiStatus === 'monthlyLimitReached') setNote('Trợ lý AI đã dùng hết hạn mức tháng; Hỗ trợ viên sẽ phản hồi bạn.');
            else if (result.aiStatus === 'notConfigured') setNote('AI chưa được cấu hình; tin nhắn đã được chuyển đến đội hỗ trợ.');
            else if (result.aiStatus === 'unavailable') setNote('Trợ lý AI hiện chưa trả lời được; tin nhắn đã được chuyển đến đội hỗ trợ.');
            else if (result.aiStatus === 'paused') setNote('Hỗ trợ viên sẽ tiếp quản cuộc trò chuyện.');
        } catch (error) {
            if (!resetExpiredConversation(error)) setNote(error.message);
        } finally {
            submit.disabled = status !== 'Open';
        }
    });

    input.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) {
            event.preventDefault();
            form.requestSubmit();
        }
    });
})();
