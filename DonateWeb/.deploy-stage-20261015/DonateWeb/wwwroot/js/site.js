// FILE: wwwroot/js/site.js
document.addEventListener("DOMContentLoaded", function () {
    // Đóng Breaking News
    window.closeBreakingNews = function() {
        const banner = document.getElementById('breakingNewsBanner');
        if (banner) {
            banner.style.display = 'none';
        }
    };

    // Toggle Password Visibility (Cho trang Profile)
    const togglePassword = document.querySelectorAll('.toggle-password');
    togglePassword.forEach(btn => {
        btn.addEventListener('click', function (e) {
            const input = this.previousElementSibling;
            if (input.type === 'password') {
                input.type = 'text';
                this.classList.replace('fa-eye', 'fa-eye-slash');
            } else {
                input.type = 'password';
                this.classList.replace('fa-eye-slash', 'fa-eye');
            }
        });
    });

    // Sao chép liên kết OBS Browser Source vào Clipboard
    window.copyToClipboard = function (text) {
        if (!text) return;
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(function () {
                alert('Đã sao chép liên kết OBS vào bộ nhớ tạm:\n' + text);
            }).catch(function () {
                fallbackCopyText(text);
            });
        } else {
            fallbackCopyText(text);
        }
    };

    function fallbackCopyText(text) {
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.style.position = 'fixed';
        textArea.style.left = '-999999px';
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        try {
            document.execCommand('copy');
            alert('Đã sao chép liên kết OBS vào bộ nhớ tạm:\n' + text);
        } catch (err) {
            prompt('Hãy bấm Ctrl+C để sao chép link OBS:', text);
        }
        document.body.removeChild(textArea);
    }
});