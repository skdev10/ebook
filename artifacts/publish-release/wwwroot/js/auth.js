// Enhanced Authentication JavaScript for ASP.NET

document.addEventListener('DOMContentLoaded', function() {
    // DOM Elements
    const signupContainer = document.getElementById('signup-container');
    const loginContainer = document.getElementById('login-container');
    const signupForm = document.getElementById('signupForm');
    const loginForm = document.getElementById('loginForm');
    const showLoginLink = document.getElementById('show-login');
    const showSignupLink = document.getElementById('show-signup');
    
    // Show login form
    showLoginLink?.addEventListener('click', function(e) {
        e.preventDefault();
        signupContainer.style.display = 'none';
        loginContainer.style.display = 'block';
    });
    
    // Show signup form
    showSignupLink?.addEventListener('click', function(e) {
        e.preventDefault();
        loginContainer.style.display = 'none';
        signupContainer.style.display = 'block';
    });
    
    // Signup form submission
    signupForm?.addEventListener('submit', async function(e) {
        e.preventDefault();
        
        const formData = new FormData(this);
        const data = Object.fromEntries(formData.entries());
        
        // Client-side validation
        if (data.password !== data.confirmPassword) {
            showToast('Passwords do not match!', 'error');
            return;
        }
        
        if (!data.terms) {
            showToast('Please accept the Terms of Service!', 'error');
            return;
        }
        
        const submitBtn = this.querySelector('button[type=\"submit\"]');
        const originalText = submitBtn.textContent;
        submitBtn.classList.add('loading');
        submitBtn.disabled = true;
        
        try {
            // Use the FormData directly
            const formData = new FormData(this);
            
            // Get anti-forgery token
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            
            const response = await fetch('/Account/Register', {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token
                },
                body: formData
            });
            
            const result = await response.json();
            
            if (result.success) {
                showToast(result.message, 'success');
                setTimeout(() => {
                    signupContainer.style.display = 'none';
                    loginContainer.style.display = 'block';
                    document.getElementById('loginEmail').value = data.email;
                }, 1500);
            } else {
                showToast(result.message, 'error');
            }
        } catch (error) {
            showToast('Registration failed. Please try again.', 'error');
        } finally {
            submitBtn.classList.remove('loading');
            submitBtn.disabled = false;
        }
    });
    
    // Login form submission
    loginForm?.addEventListener('submit', async function(e) {
        e.preventDefault();
        
        const formData = new FormData(this);
        const data = Object.fromEntries(formData.entries());
        
        const submitBtn = this.querySelector('button[type=\"submit\"]');
        const originalText = submitBtn.textContent;
        submitBtn.classList.add('loading');
        submitBtn.disabled = true;
        
        try {
            // Use the FormData directly
            const formData = new FormData(this);
            
            // Get anti-forgery token
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            
            const response = await fetch('/Account/Login', {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token
                },
                body: formData
            });
            
            const result = await response.json();
            
            if (result.success) {
                showToast(result.message, 'success');
                setTimeout(() => {
                    window.location.href = '/Dashboard';
                }, 1000);
            } else {
                showToast(result.message, 'error');
            }
        } catch (error) {
            showToast('Login failed. Please try again.', 'error');
        } finally {
            submitBtn.classList.remove('loading');
            submitBtn.disabled = false;
        }
    });
});

// Toast notification function
function showToast(message, type = 'success') {
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
        <span class=\"toast-icon\">${getToastIcon(type)}</span>
        <span class=\"toast-message\">${message}</span>
        <button class=\"toast-close\" onclick=\"this.parentElement.classList.remove('show')\">&times;</button>
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
        default: return 'ℹ️';
    }
}

// Form validation helpers
function validateEmail(email) {
    const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return re.test(email);
}

function validatePassword(password) {
    return password.length >= 8;
}

// Export functions for global access
window.showToast = showToast;