// Enhanced Authentication JavaScript for ASP.NET Core

document.addEventListener('DOMContentLoaded', function() {
    // Initialize authentication system
    initializeAuth();
    
    // Setup form handlers
    setupFormHandlers();
    
    // Setup form switching
    setupFormSwitching();
    
    // Setup validation
    setupValidation();
    
    // Setup accessibility
    setupAccessibility();
});

// Initialize authentication system
function initializeAuth() {
    console.log('Authentication system initializing...');
    
    // Setup CSRF token
    setupCSRFToken();
    
    // Setup rate limiting
    setupRateLimiting();
    
    // Setup form persistence
    setupFormPersistence();
    
    console.log('Authentication system initialized');
}

// Setup CSRF token for requests
function setupCSRFToken() {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (token) {
        window.csrfToken = token;
    }
}

// Rate limiting for login attempts
let loginAttempts = 0;
const maxAttempts = 5;
const lockoutTime = 15 * 60 * 1000; // 15 minutes

function setupRateLimiting() {
    const storedAttempts = localStorage.getItem('loginAttempts');
    const storedLockout = localStorage.getItem('loginLockout');
    
    if (storedAttempts) {
        loginAttempts = parseInt(storedAttempts);
    }
    
    if (storedLockout) {
        const lockoutEndTime = parseInt(storedLockout);
        if (Date.now() < lockoutEndTime) {
            const remainingMinutes = Math.ceil((lockoutEndTime - Date.now()) / 1000 / 60);
            showToast(`Account locked. Try again in ${remainingMinutes} minutes.`, 'error');
            disableLoginForm();
        } else {
            // Clear expired lockout
            localStorage.removeItem('loginLockout');
            localStorage.removeItem('loginAttempts');
            loginAttempts = 0;
        }
    }
}

function disableLoginForm() {
    const loginForm = document.getElementById('loginForm');
    if (loginForm) {
        const inputs = loginForm.querySelectorAll('input, button');
        inputs.forEach(input => input.disabled = true);
    }
}

// Setup form switching between login and signup
function setupFormSwitching() {
    const showLoginLink = document.getElementById('show-login');
    const showSignupLink = document.getElementById('show-signup');
    
    showLoginLink?.addEventListener('click', function(e) {
        e.preventDefault();
        switchToLogin();
    });
    
    showSignupLink?.addEventListener('click', function(e) {
        e.preventDefault();
        switchToSignup();
    });
    
    // Handle browser back/forward
    window.addEventListener('popstate', function(e) {
        if (e.state?.form === 'login') {
            switchToLogin();
        } else if (e.state?.form === 'signup') {
            switchToSignup();
        }
    });
}

function switchToLogin() {
    const signupContainer = document.getElementById('signup-container');
    const loginContainer = document.getElementById('login-container');
    
    if (signupContainer && loginContainer) {
        signupContainer.style.display = 'none';
        loginContainer.style.display = 'flex';
        
        // Focus management
        const emailInput = document.getElementById('loginEmail');
        if (emailInput) {
            setTimeout(() => emailInput.focus(), 100);
        }
        
        // Update URL without reload
        history.pushState({ form: 'login' }, 'Login', '/Account/Login');
    }
}

function switchToSignup() {
    const signupContainer = document.getElementById('signup-container');
    const loginContainer = document.getElementById('login-container');
    
    if (signupContainer && loginContainer) {
        loginContainer.style.display = 'none';
        signupContainer.style.display = 'flex';
        
        // Focus management
        const nameInput = document.getElementById('fullName');
        if (nameInput) {
            setTimeout(() => nameInput.focus(), 100);
        }
        
        // Update URL without reload
        history.pushState({ form: 'signup' }, 'Sign Up', '/Account/Register');
    }
}

// Setup form handlers
function setupFormHandlers() {
    const signupForm = document.getElementById('signupForm');
    const loginForm = document.getElementById('loginForm');
    
    signupForm?.addEventListener('submit', handleSignupSubmission);
    loginForm?.addEventListener('submit', handleLoginSubmission);
}

// Handle signup form submission
async function handleSignupSubmission(e) {
    e.preventDefault();
    
    const form = e.target;
    const formData = new FormData(form);
    
    // Client-side validation
    const validationResult = validateSignupForm(formData);
    if (!validationResult.isValid) {
        showToast(validationResult.message, 'error');
        highlightInvalidFields(form, validationResult.fields);
        return;
    }
    
    const submitBtn = form.querySelector('button[type="submit"]');
    setButtonLoading(submitBtn, true, 'Creating Account...');
    
    try {
        const response = await fetch('/Account/Register', {
            method: 'POST',
            headers: {
                'RequestVerificationToken': window.csrfToken || ''
            },
            body: formData
        });
        
        if (response.ok) {
            // Check if redirected to login (success)
            if (response.url.includes('Login') || response.redirected) {
                showToast('Account created successfully! Please log in.', 'success');
                setTimeout(() => {
                    switchToLogin();
                    // Pre-fill email
                    const loginEmail = document.getElementById('loginEmail');
                    if (loginEmail) {
                        loginEmail.value = formData.get('Email') || '';
                    }
                }, 1500);
            } else {
                // Parse response for errors
                const responseText = await response.text();
                const errorMatch = responseText.match(/alert-danger[^>]*>([^<]+)/);
                const errorMessage = errorMatch ? errorMatch[1].trim() : 'Registration failed. Please try again.';
                showToast(errorMessage, 'error');
            }
        } else {
            showToast('Registration failed. Please check your information.', 'error');
        }
    } catch (error) {
        console.error('Registration error:', error);
        showToast('Network error. Please check your connection.', 'error');
    } finally {
        setButtonLoading(submitBtn, false, 'Create Account');
    }
}

// Handle login form submission
async function handleLoginSubmission(e) {
    e.preventDefault();
    
    // Check rate limiting
    if (loginAttempts >= maxAttempts) {
        showToast('Too many failed attempts. Please try again later.', 'error');
        return;
    }
    
    const form = e.target;
    const formData = new FormData(form);
    
    // Client-side validation
    const validationResult = validateLoginForm(formData);
    if (!validationResult.isValid) {
        showToast(validationResult.message, 'error');
        highlightInvalidFields(form, validationResult.fields);
        return;
    }
    
    const submitBtn = form.querySelector('button[type="submit"]');
    setButtonLoading(submitBtn, true, 'Signing In...');
    
    try {
        const response = await fetch('/Account/Login', {
            method: 'POST',
            headers: {
                'RequestVerificationToken': window.csrfToken || ''
            },
            body: formData
        });
        
        if (response.ok && (response.url.includes('AIGenerateBook') || response.url.includes('Dashboard') || response.redirected)) {
            showToast('Login successful! Redirecting...', 'success');
            
            // Reset login attempts on success
            loginAttempts = 0;
            localStorage.removeItem('loginAttempts');
            localStorage.removeItem('loginLockout');
            
            setTimeout(() => {
                window.location.href = '/Books/AIGenerateBook';
            }, 1000);
        } else {
            // Handle failed login
            loginAttempts++;
            localStorage.setItem('loginAttempts', loginAttempts.toString());
            
            if (loginAttempts >= maxAttempts) {
                const lockoutUntil = Date.now() + lockoutTime;
                localStorage.setItem('loginLockout', lockoutUntil.toString());
                showToast('Too many failed attempts. Account locked for 15 minutes.', 'error');
                disableLoginForm();
            } else {
                const remainingAttempts = maxAttempts - loginAttempts;
                showToast(`Invalid credentials. ${remainingAttempts} attempts remaining.`, 'error');
            }
        }
    } catch (error) {
        console.error('Login error:', error);
        showToast('Network error. Please check your connection.', 'error');
    } finally {
        setButtonLoading(submitBtn, false, 'Sign In');
    }
}

// Form validation functions
function validateSignupForm(formData) {
    const fullName = formData.get('FullName')?.trim();
    const email = formData.get('Email')?.trim();
    const password = formData.get('Password');
    const confirmPassword = formData.get('ConfirmPassword');
    
    const invalidFields = [];
    
    if (!fullName) {
        invalidFields.push('FullName');
        return { isValid: false, message: 'Full name is required.', fields: invalidFields };
    }
    
    if (!email) {
        invalidFields.push('Email');
        return { isValid: false, message: 'Email address is required.', fields: invalidFields };
    }
    
    if (!validateEmail(email)) {
        invalidFields.push('Email');
        return { isValid: false, message: 'Please enter a valid email address.', fields: invalidFields };
    }
    
    if (!password) {
        invalidFields.push('Password');
        return { isValid: false, message: 'Password is required.', fields: invalidFields };
    }
    
    if (password.length < 3) {
        invalidFields.push('Password');
        return { isValid: false, message: 'Password must be at least 3 characters long.', fields: invalidFields };
    }
    
    if (!confirmPassword) {
        invalidFields.push('ConfirmPassword');
        return { isValid: false, message: 'Please confirm your password.', fields: invalidFields };
    }
    
    if (password !== confirmPassword) {
        invalidFields.push('Password', 'ConfirmPassword');
        return { isValid: false, message: 'Passwords do not match.', fields: invalidFields };
    }
    
    return { isValid: true };
}

function validateLoginForm(formData) {
    const email = formData.get('Email')?.trim();
    const password = formData.get('Password');
    
    const invalidFields = [];
    
    if (!email) {
        invalidFields.push('Email');
        return { isValid: false, message: 'Email address is required.', fields: invalidFields };
    }
    
    if (!validateEmail(email)) {
        invalidFields.push('Email');
        return { isValid: false, message: 'Please enter a valid email address.', fields: invalidFields };
    }
    
    if (!password) {
        invalidFields.push('Password');
        return { isValid: false, message: 'Password is required.', fields: invalidFields };
    }
    
    return { isValid: true };
}

// Utility functions
function validateEmail(email) {
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return emailRegex.test(email);
}

function setButtonLoading(button, isLoading, text) {
    if (!button) return;
    
    if (isLoading) {
        button.disabled = true;
        button.classList.add('loading');
        button.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${text}`;
    } else {
        button.disabled = false;
        button.classList.remove('loading');
        button.innerHTML = `<i class="fas fa-user-plus"></i> ${text}`;
    }
}

function highlightInvalidFields(form, fields) {
    // Remove existing highlights
    form.querySelectorAll('.form-control.invalid').forEach(field => {
        field.classList.remove('invalid');
    });
    
    // Add highlights to invalid fields
    fields.forEach(fieldName => {
        const field = form.querySelector(`[name="${fieldName}"]`);
        if (field) {
            field.classList.add('invalid');
            setTimeout(() => field.classList.remove('invalid'), 3000);
        }
    });
}

// Setup real-time validation
function setupValidation() {
    const forms = document.querySelectorAll('form');
    
    forms.forEach(form => {
        const inputs = form.querySelectorAll('input[required]');
        
        inputs.forEach(input => {
            input.addEventListener('blur', function() {
                validateField(this);
            });
            
            input.addEventListener('input', function() {
                if (this.classList.contains('invalid')) {
                    validateField(this);
                }
            });
        });
    });
}

function validateField(field) {
    const value = field.value.trim();
    const isValid = value.length > 0 && (field.type !== 'email' || validateEmail(value));
    
    field.classList.toggle('invalid', !isValid);
    return isValid;
}

// Setup form persistence
function setupFormPersistence() {
    const forms = document.querySelectorAll('form');
    
    forms.forEach(form => {
        const inputs = form.querySelectorAll('input:not([type="password"])');
        
        // Load saved data
        inputs.forEach(input => {
            const savedValue = localStorage.getItem(`form_${form.id}_${input.name}`);
            if (savedValue && input.type !== 'password') {
                input.value = savedValue;
            }
        });
        
        // Save data on input
        inputs.forEach(input => {
            input.addEventListener('input', function() {
                if (this.type !== 'password') {
                    localStorage.setItem(`form_${form.id}_${this.name}`, this.value);
                }
            });
        });
    });
}

// Setup accessibility features
function setupAccessibility() {
    // Add keyboard navigation
    document.addEventListener('keydown', function(e) {
        // Enter key on form links
        if (e.key === 'Enter' && e.target.matches('#show-login, #show-signup')) {
            e.target.click();
        }
        
        // Escape to clear forms
        if (e.key === 'Escape') {
            const activeForm = document.querySelector('form:not([style*="none"])');
            if (activeForm) {
                activeForm.reset();
            }
        }
    });
    
    // Add ARIA labels
    const forms = document.querySelectorAll('form');
    forms.forEach(form => {
        const inputs = form.querySelectorAll('input');
        inputs.forEach(input => {
            if (!input.getAttribute('aria-label') && !input.getAttribute('aria-labelledby')) {
                const label = form.querySelector(`label[for="${input.id}"]`);
                if (label) {
                    input.setAttribute('aria-labelledby', label.id || `label_${input.id}`);
                    if (!label.id) {
                        label.id = `label_${input.id}`;
                    }
                }
            }
        });
    });
}

// Toast notification system
function showToast(message, type = 'info', duration = 5000) {
    // Remove existing toasts
    document.querySelectorAll('.toast.show').forEach(toast => {
        toast.classList.remove('show');
    });
    
    // Create or get toast element
    let toast = document.getElementById('toast');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'toast';
        toast.className = 'toast';
        document.body.appendChild(toast);
    }
    
    // Set toast content and type
    toast.className = `toast ${type}`;
    toast.innerHTML = `
        <div style="display: flex; align-items: center; gap: 0.75rem;">
            <span class="toast-icon">${getToastIcon(type)}</span>
            <span class="toast-message">${message}</span>
            <button class="toast-close" onclick="this.parentElement.parentElement.classList.remove('show')" 
                    style="background: none; border: none; font-size: 1.2rem; cursor: pointer; margin-left: auto; opacity: 0.7;" 
                    aria-label="Close notification">&times;</button>
        </div>
    `;
    
    // Show toast with animation
    setTimeout(() => toast.classList.add('show'), 100);
    
    // Auto-hide toast
    setTimeout(() => {
        if (toast.classList.contains('show')) {
            toast.classList.remove('show');
        }
    }, duration);
}

function getToastIcon(type) {
    const icons = {
        success: '✅',
        error: '❌',
        warning: '⚠️',
        info: 'ℹ️'
    };
    return icons[type] || icons.info;
}

// Export functions for global access
window.showToast = showToast;
window.switchToLogin = switchToLogin;
window.switchToSignup = switchToSignup;