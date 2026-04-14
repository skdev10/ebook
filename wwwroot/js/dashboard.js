// Enhanced Dashboard JavaScript for ASP.NET
document.addEventListener('DOMContentLoaded', function() {
    // Initialize application
    initializeApp();
    
    // Initialize theme
    initializeTheme();
    
    // Setup mobile menu
    setupMobileMenu();
    
    // Setup sidebar overlay
    setupSidebarOverlay();
    
    // Initialize CSRF token for AJAX requests
    setupCSRFToken();
    
    // Setup user session management
    setupSessionManagement();
    
    // Animate counters if present
    if (document.querySelectorAll('.stat-number[data-target]').length > 0) {
        animateCounters();
    }
});

// Application initialization
function initializeApp() {
    console.log('Dashboard initializing...');
    
    // Set active menu item based on current path
    setActiveMenuItem();
    
    // Setup global error handling
    setupGlobalErrorHandling();
    
    // Setup auto-save functionality
    setupAutoSave();
    
    // Setup theme toggle button
    setupThemeToggle();
    
    console.log('Dashboard initialized successfully');
}

// Set active menu item
function setActiveMenuItem() {
    const currentPath = window.location.pathname.toLowerCase();
    const menuLinks = document.querySelectorAll('.sidebar-menu a');
    
    menuLinks.forEach(link => {
        const linkPath = link.getAttribute('href')?.toLowerCase();
        if (linkPath === currentPath || 
            (currentPath.includes('/dashboard') && linkPath?.includes('/dashboard'))) {
            link.classList.add('active');
            // Add breadcrumb indicator
            link.setAttribute('aria-current', 'page');
        } else {
            link.classList.remove('active');
            link.removeAttribute('aria-current');
        }
    });
}

// Setup mobile menu functionality
function setupMobileMenu() {
    const menuToggle = document.getElementById('menuToggle');
    const sidebar = document.getElementById('sidebar');
    
    if (menuToggle && sidebar) {
        menuToggle.addEventListener('click', function() {
            const isActive = sidebar.classList.toggle('active');
            const overlay = document.querySelector('.sidebar-overlay');
            
            if (overlay) {
                overlay.classList.toggle('active', isActive);
            }
            
            // Update ARIA attributes
            menuToggle.setAttribute('aria-expanded', isActive);
            sidebar.setAttribute('aria-hidden', !isActive);
            
            // Prevent body scroll when sidebar is open
            document.body.style.overflow = isActive ? 'hidden' : '';
        });
    }
}

// Setup sidebar overlay
function setupSidebarOverlay() {
    let overlay = document.querySelector('.sidebar-overlay');
    
    if (!overlay && window.innerWidth <= 768) {
        overlay = document.createElement('div');
        overlay.className = 'sidebar-overlay';
        document.body.appendChild(overlay);
        
        overlay.addEventListener('click', function() {
            const sidebar = document.getElementById('sidebar');
            const menuToggle = document.getElementById('menuToggle');
            
            if (sidebar) {
                sidebar.classList.remove('active');
                overlay.classList.remove('active');
                document.body.style.overflow = '';
                
                if (menuToggle) {
                    menuToggle.setAttribute('aria-expanded', 'false');
                }
            }
        });
    }
}

// CSRF Token setup for AJAX requests
function setupCSRFToken() {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (token) {
        // Set default AJAX headers
        if (typeof $ !== 'undefined') {
            $.ajaxSetup({
                beforeSend: function(xhr) {
                    xhr.setRequestHeader('RequestVerificationToken', token);
                }
            });
        }
        
        // Store for vanilla JS AJAX calls
        window.csrfToken = token;
    }
}

// Session management
function setupSessionManagement() {
    // Check session every 5 minutes
    setInterval(checkSession, 5 * 60 * 1000);
    
    // Warn before session expires
    setTimeout(showSessionWarning, 25 * 60 * 1000); // 25 minutes
}

function checkSession() {
    fetch('/Account/CheckSession', {
        method: 'GET',
        credentials: 'same-origin'
    })
    .then(response => {
        if (!response.ok && response.status === 401) {
            showToast('Your session has expired. Please log in again.', 'warning');
            setTimeout(() => {
                window.location.href = '/Account/Login';
            }, 3000);
        }
    })
    .catch(error => {
        console.warn('Session check failed:', error);
    });
}

function showSessionWarning() {
    showToast('Your session will expire in 5 minutes. Please save your work.', 'warning');
}

// Global error handling
function setupGlobalErrorHandling() {
    window.addEventListener('error', function(e) {
        console.error('Global error:', e.error);
        showToast('An unexpected error occurred. Please refresh the page.', 'error');
    });
    
    window.addEventListener('unhandledrejection', function(e) {
        console.error('Unhandled promise rejection:', e.reason);
        showToast('A network error occurred. Please try again.', 'error');
    });
}

// Auto-save functionality
function setupAutoSave() {
    const forms = document.querySelectorAll('form[data-autosave]');
    
    forms.forEach(form => {
        const inputs = form.querySelectorAll('input, textarea, select');
        
        inputs.forEach(input => {
            input.addEventListener('input', debounce(() => {
                saveFormData(form);
            }, 2000));
        });
    });
}

function saveFormData(form) {
    const formData = new FormData(form);
    const data = Object.fromEntries(formData.entries());
    
    localStorage.setItem(`autosave_${form.id}`, JSON.stringify(data));
    showToast('Draft saved automatically', 'info');
}

// Theme toggle setup
function setupThemeToggle() {
    const themeToggle = document.getElementById('themeToggle');
    if (themeToggle) {
        themeToggle.addEventListener('click', toggleTheme);
    }
}

// Utility functions
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// AJAX helper functions
function makeAjaxRequest(url, options = {}) {
    const defaultOptions = {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest'
        },
        credentials: 'same-origin'
    };
    
    if (window.csrfToken) {
        defaultOptions.headers['RequestVerificationToken'] = window.csrfToken;
    }
    
    return fetch(url, { ...defaultOptions, ...options })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .catch(error => {
            console.error('AJAX request failed:', error);
            showToast('Request failed. Please try again.', 'error');
            throw error;
        });
}

// Theme Management
function toggleTheme() {
    const html = document.documentElement;
    const currentTheme = html.getAttribute('data-theme') || 'light';
    const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
    
    html.setAttribute('data-theme', newTheme);
    localStorage.setItem('theme', newTheme);
    
    const themeToggle = document.querySelector('.theme-toggle');
    if (themeToggle) {
        themeToggle.textContent = newTheme === 'dark' ? '☀️' : '🌙';
    }
    
    showToast(`Switched to ${newTheme} mode`, 'success');
}

function initializeTheme() {
    const savedTheme = localStorage.getItem('theme') || 'light';
    document.documentElement.setAttribute('data-theme', savedTheme);
    
    const themeToggle = document.querySelector('.theme-toggle');
    if (themeToggle) {
        themeToggle.textContent = savedTheme === 'dark' ? '☀️' : '🌙';
    }
}

// Modal Functions
function openLiveChat() {
    const modal = document.getElementById('liveChatModal');
    if (modal) {
        modal.style.display = 'flex';
        document.body.style.overflow = 'hidden';
        
        // Focus on chat input
        const chatInput = document.getElementById('chatInput');
        if (chatInput) {
            setTimeout(() => chatInput.focus(), 100);
        }
    }
}

function closeLiveChat() {
    const modal = document.getElementById('liveChatModal');
    if (modal) {
        modal.style.display = 'none';
        document.body.style.overflow = 'auto';
    }
}

function sendMessage() {
    const input = document.getElementById('chatInput');
    const messagesContainer = document.getElementById('chatMessages');
    
    if (!input || !messagesContainer || !input.value.trim()) return;
    
    const message = input.value.trim();
    
    // Add user message
    const userMessage = document.createElement('div');
    userMessage.className = 'message user';
    userMessage.style.cssText = 'background: var(--primary-color); color: white; padding: 1rem; border-radius: 0.5rem; margin-bottom: 1rem; margin-left: auto; max-width: 80%;';
    userMessage.innerHTML = `<strong>You:</strong> ${message}`;
    messagesContainer.appendChild(userMessage);
    
    // Clear input
    input.value = '';
    
    // Simulate support response
    setTimeout(() => {
        const supportMessage = document.createElement('div');
        supportMessage.className = 'message support';
        supportMessage.style.cssText = 'background: var(--bg-card); padding: 1rem; border-radius: 0.5rem; margin-bottom: 1rem; border-left: 4px solid var(--primary-color);';
        supportMessage.innerHTML = `<strong>Support Agent:</strong> Thank you for your message. Our team will get back to you shortly!`;
        messagesContainer.appendChild(supportMessage);
        
        // Scroll to bottom
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }, 1000);
    
    // Scroll to bottom
    messagesContainer.scrollTop = messagesContainer.scrollHeight;
}

function showHowToStart() {
    const modal = document.getElementById('howToStartModal');
    if (modal) {
        modal.style.display = 'flex';
        document.body.style.overflow = 'hidden';
    }
}

function closeHowToStart() {
    const modal = document.getElementById('howToStartModal');
    if (modal) {
        modal.style.display = 'none';
        document.body.style.overflow = 'auto';
    }
}

function scheduleCall() {
    showToast('Redirecting to calendar booking...', 'info');
    setTimeout(() => {
        showToast('Calendar booking system would open here', 'success');
    }, 1000);
}

// Counter Animation
function animateCounters() {
    const counters = document.querySelectorAll('.stat-number[data-target]');
    
    counters.forEach(counter => {
        const target = parseInt(counter.getAttribute('data-target'));
        const duration = 2000;
        const increment = target / (duration / 16);
        let current = 0;
        
        const timer = setInterval(() => {
            current += increment;
            if (current >= target) {
                current = target;
                clearInterval(timer);
            }
            counter.textContent = Math.floor(current).toLocaleString();
        }, 16);
    });
}

// Toast Notifications
function showToast(message, type = 'info') {
    // Remove existing toast
    const existingToast = document.querySelector('.toast.show');
    if (existingToast) {
        existingToast.classList.remove('show');
    }
    
    // Create or update toast
    let toast = document.getElementById('toast');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'toast';
        toast.className = 'toast';
        document.body.appendChild(toast);
    }
    
    toast.className = `toast ${type}`;
    toast.innerHTML = `
        <div style=\"display: flex; align-items: center; gap: 0.75rem;\">
            <span class=\"toast-icon\">${getToastIcon(type)}</span>
            <span class=\"toast-message\">${message}</span>
            <button class=\"toast-close\" onclick=\"this.parentElement.parentElement.classList.remove('show')\" style=\"background: none; border: none; font-size: 1.2rem; cursor: pointer; margin-left: auto; opacity: 0.7;\">&times;</button>
        </div>
    `;
    
    // Show toast
    setTimeout(() => toast.classList.add('show'), 100);
    
    // Auto hide after 5 seconds
    setTimeout(() => {
        if (toast.classList.contains('show')) {
            toast.classList.remove('show');
        }
    }, 5000);
}

function getToastIcon(type) {
    switch (type) {
        case 'success': return '✅';
        case 'error': return '❌';
        case 'warning': return '⚠️';
        case 'info': return 'ℹ️';
        default: return 'ℹ️';
    }
}

// Keyboard shortcuts
document.addEventListener('keydown', function(e) {
    // Alt + C for live chat
    if (e.altKey && e.key === 'c') {
        e.preventDefault();
        openLiveChat();
    }
    
    // Alt + H for how to start
    if (e.altKey && e.key === 'h') {
        e.preventDefault();
        showHowToStart();
    }
    
    // Alt + T for theme toggle
    if (e.altKey && e.key === 't') {
        e.preventDefault();
        toggleTheme();
    }
    
    // Escape to close modals
    if (e.key === 'Escape') {
        closeLiveChat();
        closeHowToStart();
        if (typeof closeUpgradeModal === 'function') {
            closeUpgradeModal();
        }
    }
});

// Chat input enter key handler
document.addEventListener('keydown', function(e) {
    if (e.key === 'Enter' && e.target.id === 'chatInput') {
        e.preventDefault();
        sendMessage();
    }
});

// Responsive utilities
function isMobile() {
    return window.innerWidth <= 768;
}

function isTablet() {
    return window.innerWidth > 768 && window.innerWidth <= 1024;
}

// Window resize handler
window.addEventListener('resize', function() {
    const sidebar = document.getElementById('sidebar');
    if (window.innerWidth > 768) {
        sidebar.classList.remove('active');
    }
});

// Performance monitoring
window.addEventListener('load', function() {
    const loadTime = performance.now();
    console.log(`Dashboard loaded in ${Math.round(loadTime)}ms`);
    
    // Show welcome message for first-time users
    if (!localStorage.getItem('dashboardWelcomed')) {
        setTimeout(() => {
            showToast('Welcome to your eBook dashboard! 📚', 'success');
            localStorage.setItem('dashboardWelcomed', 'true');
        }, 1000);
    }
});

// Export functions for global access
window.toggleTheme = toggleTheme;
window.openLiveChat = openLiveChat;
window.closeLiveChat = closeLiveChat;
window.sendMessage = sendMessage;
window.showHowToStart = showHowToStart;
window.closeHowToStart = closeHowToStart;
window.scheduleCall = scheduleCall;
window.showToast = showToast;
window.animateCounters = animateCounters;