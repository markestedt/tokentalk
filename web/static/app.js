// Application state
let currentPage = 'statistics';
let currentDays = 7;
let currentHistoryPage = 0;
const historyPageSize = 50;
let ws = null;
let reconnectTimer = null;

// Charts
let dictationsChart = null;
let wordsChart = null;

// Initialize app
document.addEventListener('DOMContentLoaded', () => {
    initNavigation();
    initWebSocket();
    loadConfig();
    loadStatistics();

    // Setup event listeners
    document.getElementById('configForm').addEventListener('submit', handleConfigSubmit);

    // Statistics page
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            document.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
            e.target.classList.add('active');
            currentDays = parseInt(e.target.dataset.days);
            loadStatistics();
        });
    });

    // History page
    document.getElementById('refreshHistory').addEventListener('click', loadHistory);
    document.getElementById('prevPage').addEventListener('click', () => {
        if (currentHistoryPage > 0) {
            currentHistoryPage--;
            loadHistory();
        }
    });
    document.getElementById('nextPage').addEventListener('click', () => {
        currentHistoryPage++;
        loadHistory();
    });
});

// Navigation
function initNavigation() {
    document.querySelectorAll('.tab').forEach(tab => {
        tab.addEventListener('click', (e) => {
            const page = e.target.dataset.page;
            switchPage(page);
        });
    });
}

function switchPage(page) {
    // Update tabs
    document.querySelectorAll('.tab').forEach(tab => {
        tab.classList.toggle('active', tab.dataset.page === page);
    });

    // Update pages
    document.querySelectorAll('.page').forEach(p => {
        p.classList.toggle('active', p.id === page + 'Page');
    });

    currentPage = page;

    // Load page-specific data
    if (page === 'statistics') {
        loadStatistics();
    } else if (page === 'history') {
        currentHistoryPage = 0;
        loadHistory();
    }
}

// WebSocket
function initWebSocket() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}/ws`;

    ws = new WebSocket(wsUrl);

    ws.onopen = () => {
        console.log('WebSocket connected');
        clearTimeout(reconnectTimer);
    };

    ws.onmessage = (event) => {
        const message = JSON.parse(event.data);
        handleWebSocketMessage(message);
    };

    ws.onerror = (error) => {
        console.error('WebSocket error:', error);
    };

    ws.onclose = () => {
        console.log('WebSocket disconnected');
        // Attempt to reconnect after 3 seconds
        reconnectTimer = setTimeout(initWebSocket, 3000);
    };
}

function handleWebSocketMessage(message) {
    if (message.type === 'status') {
        updateStatus(message.data.status);
    } else if (message.type === 'dictation') {
        handleNewDictation(message.data);
    }
}

function updateStatus(status) {
    const statusDot = document.getElementById('statusDot');
    const statusText = document.getElementById('statusText');

    statusDot.className = 'status-dot';

    switch (status) {
        case 'recording':
            statusDot.classList.add('recording');
            statusText.textContent = 'Recording...';
            break;
        case 'processing':
            statusDot.classList.add('processing');
            statusText.textContent = 'Processing...';
            break;
        default:
            statusText.textContent = 'Idle';
    }
}

function handleNewDictation(data) {
    // Refresh statistics if on statistics page
    if (currentPage === 'statistics') {
        loadStatistics();
    }

    // Refresh history if on first page of history
    if (currentPage === 'history' && currentHistoryPage === 0) {
        loadHistory();
    }
}

// Configuration
async function loadConfig() {
    try {
        const response = await fetch('/api/config');
        const config = await response.json();

        document.getElementById('hotkey').value = config.hotkey;
        document.getElementById('provider').value = config.provider;
        document.getElementById('model').value = config.model;
        document.getElementById('language').value = config.language;
        document.getElementById('prompt').value = config.prompt;
        document.getElementById('audioDevice').value = config.audioDevice;
        document.getElementById('silenceThreshold').value = config.silenceThreshold;
        document.getElementById('webEnabled').checked = config.webEnabled;
        document.getElementById('webPort').value = config.webPort;
        document.getElementById('developerMode').checked = config.developerMode;
        document.getElementById('postprocessingEnabled').checked = config.postprocessingEnabled;
        document.getElementById('postprocessingCommands').checked = config.postprocessingCommands;
        document.getElementById('postprocessingGrammar').checked = config.postprocessingGrammar;
        document.getElementById('grammarProvider').value = config.grammarProvider;
        document.getElementById('grammarModel').value = config.grammarModel;
    } catch (error) {
        console.error('Failed to load config:', error);
        showMessage('error', 'Failed to load configuration');
    }
}

async function handleConfigSubmit(e) {
    e.preventDefault();

    const formData = new FormData(e.target);
    const config = {
        hotkey: formData.get('hotkey'),
        provider: formData.get('provider'),
        model: formData.get('model'),
        language: formData.get('language'),
        prompt: formData.get('prompt'),
        audioDevice: parseInt(formData.get('audioDevice')),
        silenceThreshold: parseFloat(formData.get('silenceThreshold')),
        webEnabled: formData.get('webEnabled') === 'on',
        webPort: parseInt(formData.get('webPort')),
        developerMode: formData.get('developerMode') === 'on',
        postprocessingEnabled: formData.get('postprocessingEnabled') === 'on',
        postprocessingCommands: formData.get('postprocessingCommands') === 'on',
        postprocessingGrammar: formData.get('postprocessingGrammar') === 'on',
        grammarProvider: formData.get('grammarProvider'),
        grammarModel: formData.get('grammarModel'),
    };

    const apiKey = formData.get('apiKey');
    if (apiKey) {
        config.apiKey = apiKey;
    }

    try {
        const response = await fetch('/api/config', {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(config),
        });

        if (response.ok) {
            showMessage('success', 'Configuration saved successfully');
        } else {
            showMessage('error', 'Failed to save configuration');
        }
    } catch (error) {
        console.error('Failed to save config:', error);
        showMessage('error', 'Failed to save configuration');
    }
}

function showMessage(type, message) {
    const messageEl = document.getElementById('saveMessage');
    messageEl.textContent = message;
    messageEl.className = `message ${type}`;

    setTimeout(() => {
        messageEl.className = 'message';
    }, 5000);
}

// Statistics
async function loadStatistics() {
    try {
        const response = await fetch(`/api/stats?days=${currentDays}`);
        const data = await response.json();

        updateOverallStats(data.overall);
        updateCharts(data.daily);
        updateProviderTable(data.provider);
    } catch (error) {
        console.error('Failed to load statistics:', error);
    }
}

function updateOverallStats(stats) {
    document.getElementById('totalDictations').textContent = stats.TotalDictations || 0;
    document.getElementById('totalWords').textContent = stats.TotalWords || 0;

    const successRate = stats.TotalDictations > 0
        ? Math.round((stats.SuccessCount / stats.TotalDictations) * 100)
        : 0;
    document.getElementById('successRate').textContent = `${successRate}%`;

    const avgLatency = Math.round(stats.AvgTotalLatencyMs || 0);
    document.getElementById('avgLatency').textContent = `${avgLatency}ms`;
}

function updateCharts(dailyStats) {
    // Reverse the data so oldest is first
    const data = [...dailyStats].reverse();

    const labels = data.map(d => d.Date);
    const dictations = data.map(d => d.TotalDictations);
    const words = data.map(d => d.TotalWords);

    // Dictations chart
    const dictationsCtx = document.getElementById('dictationsChart').getContext('2d');
    if (dictationsChart) {
        dictationsChart.destroy();
    }
    dictationsChart = new Chart(dictationsCtx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Dictations',
                data: dictations,
                borderColor: '#4a90e2',
                backgroundColor: 'rgba(74, 144, 226, 0.1)',
                tension: 0.4,
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        precision: 0
                    }
                }
            }
        }
    });

    // Words chart
    const wordsCtx = document.getElementById('wordsChart').getContext('2d');
    if (wordsChart) {
        wordsChart.destroy();
    }
    wordsChart = new Chart(wordsCtx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Words',
                data: words,
                borderColor: '#5cb85c',
                backgroundColor: 'rgba(92, 184, 92, 0.1)',
                tension: 0.4,
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        precision: 0
                    }
                }
            }
        }
    });
}

function updateProviderTable(providerStats) {
    const tbody = document.getElementById('providerTableBody');
    tbody.innerHTML = '';

    if (providerStats.length === 0) {
        tbody.innerHTML = '<tr><td colspan="5" style="text-align: center;">No data available</td></tr>';
        return;
    }

    providerStats.forEach(p => {
        const row = document.createElement('tr');

        const successRate = p.TotalDictations > 0
            ? Math.round((p.SuccessCount / p.TotalDictations) * 100)
            : 0;

        row.innerHTML = `
            <td>${p.Provider}</td>
            <td>${p.TotalDictations}</td>
            <td>${p.TotalWords}</td>
            <td>${successRate}%</td>
            <td>${Math.round(p.AvgLatencyMs)}ms</td>
        `;

        tbody.appendChild(row);
    });
}

// History
async function loadHistory() {
    try {
        const offset = currentHistoryPage * historyPageSize;
        const response = await fetch(`/api/history?limit=${historyPageSize}&offset=${offset}`);
        const data = await response.json();

        updateHistoryList(data.dictations);
        updatePagination(data.total);
    } catch (error) {
        console.error('Failed to load history:', error);
    }
}

function updateHistoryList(dictations) {
    const list = document.getElementById('historyList');
    list.innerHTML = '';

    if (!dictations || dictations.length === 0) {
        list.innerHTML = '<div class="empty-state"><p>No dictations yet</p><small>Your dictation history will appear here</small></div>';
        return;
    }

    dictations.forEach(d => {
        const item = createHistoryItem(d);
        list.appendChild(item);
    });
}

function createHistoryItem(dictation) {
    const div = document.createElement('div');
    div.className = 'history-item' + (dictation.Success ? '' : ' error');

    const timestamp = new Date(dictation.Timestamp).toLocaleString();
    const latencyText = `${Math.round(dictation.TotalLatencyMs)}ms`;

    div.innerHTML = `
        <div class="history-header">
            <div class="history-timestamp">${timestamp}</div>
        </div>
        <div class="history-text">${escapeHtml(dictation.TranscribedText || dictation.ErrorMessage)}</div>
        <div class="history-meta">
            <span><strong>Words:</strong> ${dictation.WordCount}</span>
            <span><strong>Provider:</strong> ${dictation.Provider}</span>
            <span><strong>Model:</strong> ${dictation.Model}</span>
            <span><strong>Latency:</strong> ${latencyText}</span>
            ${dictation.Success ? '<span style="color: #5cb85c">✓ Success</span>' : '<span style="color: #d9534f">✗ Failed</span>'}
        </div>
        <div class="history-actions">
            <button class="btn-small btn-delete" onclick="deleteHistoryItem(${dictation.ID})">Delete</button>
        </div>
    `;

    // Click to copy text
    div.addEventListener('click', (e) => {
        if (e.target.tagName !== 'BUTTON' && dictation.Success) {
            copyToClipboard(dictation.TranscribedText);
        }
    });

    return div;
}

function updatePagination(total) {
    const totalPages = Math.ceil(total / historyPageSize);
    document.getElementById('pageInfo').textContent = `Page ${currentHistoryPage + 1} of ${totalPages}`;

    document.getElementById('prevPage').disabled = currentHistoryPage === 0;
    document.getElementById('nextPage').disabled = currentHistoryPage >= totalPages - 1;
}

async function deleteHistoryItem(id) {
    if (!confirm('Are you sure you want to delete this dictation?')) {
        return;
    }

    try {
        const response = await fetch(`/api/history/${id}`, {
            method: 'DELETE',
        });

        if (response.ok) {
            loadHistory();
        } else {
            alert('Failed to delete dictation');
        }
    } catch (error) {
        console.error('Failed to delete dictation:', error);
        alert('Failed to delete dictation');
    }
}

// Utilities
function copyToClipboard(text) {
    navigator.clipboard.writeText(text).then(() => {
        console.log('Copied to clipboard');
    }).catch(err => {
        console.error('Failed to copy:', err);
    });
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Dictionary Management
let dictionaryEntries = [];

document.getElementById('manageDictionary').addEventListener('click', openDictionaryModal);
document.getElementById('closeDictionary').addEventListener('click', closeDictionaryModal);
document.getElementById('addSimpleTerm').addEventListener('click', () => addDictionaryEntry(false));
document.getElementById('addMapping').addEventListener('click', () => addDictionaryEntry(true));
document.getElementById('saveDictionary').addEventListener('click', saveDictionary);

async function openDictionaryModal() {
    try {
        const response = await fetch('/api/dictionary');
        const data = await response.json();
        dictionaryEntries = data.entries || [];
        renderDictionary();
        document.getElementById('dictionaryModal').style.display = 'block';
    } catch (error) {
        console.error('Failed to load dictionary:', error);
        alert('Failed to load dictionary');
    }
}

function closeDictionaryModal() {
    document.getElementById('dictionaryModal').style.display = 'none';
}

function renderDictionary() {
    const list = document.getElementById('dictionaryList');
    list.innerHTML = '';

    if (dictionaryEntries.length === 0) {
        list.innerHTML = '<p style="text-align: center; color: #666;">No entries yet. Add a term or correction above.</p>';
        return;
    }

    dictionaryEntries.forEach((entry, index) => {
        const div = document.createElement('div');
        div.className = 'dictionary-entry';
        div.innerHTML = `
            <div class="dictionary-entry-content">
                ${entry.isMapping ? 
                    `<span class="original">${escapeHtml(entry.original)}</span> → <span class="replacement">${escapeHtml(entry.replacement)}</span>` :
                    `<span class="term">${escapeHtml(entry.replacement)}</span>`
                }
            </div>
            <button class="btn-delete" onclick="deleteDictionaryEntry(${index})">Delete</button>
        `;
        list.appendChild(div);
    });
}

function addDictionaryEntry(isMapping) {
    if (isMapping) {
        const original = prompt('Enter the misheard phrase:');
        if (!original) return;
        const replacement = prompt('Enter the correct term:');
        if (!replacement) return;
        dictionaryEntries.push({ original, replacement, isMapping: true });
    } else {
        const term = prompt('Enter the term to add:');
        if (!term) return;
        dictionaryEntries.push({ original: '', replacement: term, isMapping: false });
    }
    renderDictionary();
}

function deleteDictionaryEntry(index) {
    dictionaryEntries.splice(index, 1);
    renderDictionary();
}

async function saveDictionary() {
    try {
        const response = await fetch('/api/dictionary', {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({ entries: dictionaryEntries }),
        });

        if (response.ok) {
            alert('Dictionary saved successfully!');
            closeDictionaryModal();
        } else {
            alert('Failed to save dictionary');
        }
    } catch (error) {
        console.error('Failed to save dictionary:', error);
        alert('Failed to save dictionary');
    }
}
