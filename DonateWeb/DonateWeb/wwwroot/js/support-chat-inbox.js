(() => {
    const root = document.getElementById('supportInbox');
    if (!root) return;

    const list = document.getElementById('supportInboxList');
    const empty = document.getElementById('supportInboxEmpty');
    const active = document.getElementById('supportInboxActive');
    const visitorLabel = document.getElementById('supportInboxVisitor');
    const userLabel = document.getElementById('supportInboxUser');
    const aiModeLabel = document.getElementById('supportInboxAiMode');
    const messagesView = document.getElementById('supportInboxMessages');
    const form = document.getElementById('supportInboxForm');
    const input = document.getElementById('supportInboxInput');
    const note = document.getElementById('supportInboxNote');
    const closeButton = document.getElementById('supportInboxCloseConversation');
    const csrf = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    let status = 'Open';
    let selectedId = null;
    let lastMessageId = 0;
    let selectedConversation = null;

    const request = async (url, options = {}) => {
        const response = await fetch(url, { ...options, credentials: 'same-origin', headers: { 'Accept': 'application/json', ...(options.headers || {}) } });
        if (!response.ok) throw new Error(response.status === 409 ? 'Cuộc trò chuyện đã đóng.' : 'Không thể tải hộp thư.');
        return response.status === 204 ? null : response.json();
    };

    function appendMessage(message) {
        const row = document.createElement('article');
        row.className = `support-inbox-message ${message.isFromSupport ? 'from-support' : 'from-visitor'}${message.isAi ? ' from-ai' : ''}`;
        const heading = document.createElement('strong');
        heading.textContent = message.isAi ? 'Trợ lý AI DonateWeb' : message.isFromSupport ? 'Hỗ trợ viên' : message.senderName;
        const text = document.createElement('div');
        text.textContent = message.content;
        const time = document.createElement('time');
        time.textContent = new Date(message.createdAt).toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' });
        row.append(heading, text, time);
        messagesView.appendChild(row);
        lastMessageId = Math.max(lastMessageId, Number(message.id));
        messagesView.scrollTop = messagesView.scrollHeight;
    }

    async function loadList() {
        try {
            const conversations = await request(`${root.dataset.listUrl}?status=${status}`);
            list.replaceChildren();
            if (!conversations.length) {
                const emptyItem = document.createElement('p');
                emptyItem.className = 'text-muted p-3 mb-0';
                emptyItem.textContent = status === 'Open' ? 'Chưa có yêu cầu hỗ trợ mới.' : 'Chưa có cuộc trò chuyện đã đóng.';
                list.appendChild(emptyItem);
                return;
            }
            conversations.forEach(conversation => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = `support-inbox-item${selectedId === conversation.id ? ' active' : ''}`;
                const title = document.createElement('strong');
                title.textContent = conversation.visitorName;
                const preview = document.createElement('span');
                preview.textContent = conversation.lastMessage;
                const meta = document.createElement('small');
                const time = new Date(conversation.lastMessageAt).toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' });
                meta.textContent = conversation.unreadCount > 0 ? `${time} · ${conversation.unreadCount} tin mới` : time;
                button.append(title, preview, meta);
                button.addEventListener('click', () => selectConversation(conversation));
                list.appendChild(button);
            });
        } catch (error) {
            list.textContent = error.message;
        }
    }

    async function selectConversation(conversation) {
        selectedConversation = conversation;
        selectedId = conversation.id;
        lastMessageId = 0;
        visitorLabel.textContent = conversation.visitorName;
        userLabel.textContent = conversation.userEmail || 'Khách chưa đăng nhập';
        aiModeLabel.textContent = conversation.aiPaused ? 'AI đã nhường cho Hỗ trợ viên' : 'AI đang trả lời ban đầu';
        empty.hidden = true;
        active.hidden = false;
        messagesView.replaceChildren();
        input.disabled = conversation.status !== 'Open';
        form.querySelector('button[type="submit"]').disabled = conversation.status !== 'Open';
        closeButton.hidden = conversation.status !== 'Open';
        note.textContent = '';
        await refreshMessages(true);
        await loadList();
    }

    async function refreshMessages(initial = false) {
        if (!selectedId) return;
        const url = `${root.dataset.messagesUrl}/${selectedId}?afterMessageId=${initial ? 0 : lastMessageId}`;
        const data = await request(url);
        data.messages.forEach(appendMessage);
        aiModeLabel.textContent = data.aiPaused ? 'AI đã nhường cho Hỗ trợ viên' : 'AI đang trả lời ban đầu';
        if (data.status !== 'Open') {
            input.disabled = true;
            form.querySelector('button[type="submit"]').disabled = true;
            closeButton.hidden = true;
            note.textContent = 'Cuộc trò chuyện đã đóng.';
        }
    }

    document.querySelectorAll('.support-inbox-tabs button').forEach(button => {
        button.addEventListener('click', () => {
            status = button.dataset.status;
            document.querySelectorAll('.support-inbox-tabs button').forEach(tab => tab.classList.toggle('active', tab === button));
            selectedId = null;
            selectedConversation = null;
            active.hidden = true;
            empty.hidden = false;
            loadList();
        });
    });

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!selectedId || !input.value.trim()) return;
        const submit = form.querySelector('button[type="submit"]');
        submit.disabled = true;
        try {
            const body = new URLSearchParams({ conversationId: String(selectedId), message: input.value.trim(), __RequestVerificationToken: csrf });
            await request(root.dataset.sendUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': csrf },
                body
            });
            if (selectedConversation) selectedConversation.aiPaused = true;
            aiModeLabel.textContent = 'AI đã nhường cho Hỗ trợ viên';
            input.value = '';
            await refreshMessages();
            await loadList();
        } catch (error) {
            note.textContent = error.message;
        } finally {
            submit.disabled = !selectedConversation || selectedConversation.status !== 'Open';
        }
    });

    input.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) {
            event.preventDefault();
            if (!input.disabled && !form.querySelector('button[type="submit"]').disabled) {
                form.requestSubmit();
            }
        }
    });

    closeButton.addEventListener('click', async () => {
        if (!selectedId || !window.confirm('Đóng cuộc trò chuyện này?')) return;
        const body = new URLSearchParams({ conversationId: String(selectedId), __RequestVerificationToken: csrf });
        try {
            await request(root.dataset.closeUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': csrf },
                body
            });
            if (selectedConversation) selectedConversation.status = 'Closed';
            input.disabled = true;
            form.querySelector('button[type="submit"]').disabled = true;
            closeButton.hidden = true;
            note.textContent = 'Cuộc trò chuyện đã đóng.';
            await loadList();
        } catch (error) { note.textContent = error.message; }
    });

    loadList();
    window.setInterval(() => {
        loadList();
        if (selectedId) refreshMessages().catch(error => { note.textContent = error.message; });
    }, 4000);
})();
