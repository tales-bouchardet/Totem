const { invoke } = window.__TAURI__.core;

// ======================= LOAD JSON AND CREATE TABS =======================
let aba_content = document.querySelector("#aba-content");
const aba_wrapper = document.getElementById("aba-wrapper");

invoke("get_json").then(async json => {
if (JSON.stringify(json) == "{}") {
  let json_data = {"abas":[{"key":"Nova Aba","values":[{"titulo":"Nova Aba"},{"text":"Escreva aqui..."}]}],"tv_host":{"name":"TeamViewer Host","value":"a"}};
  await invoke("set_json", { data: json_data });
  set_edit_mode(false);
}
    // --- TeamViewer Host ---
  if (json.tv_host) {
    const tvHostBtn = document.getElementById("tv-host");
    if (tvHostBtn) {
      tvHostBtn.setAttribute("name", json.tv_host.name);
      tvHostBtn.value = json.tv_host.value;
    }
  }
json.abas.forEach(({ key, values }) => {
  let aba = document.createElement("div");
  aba.className = "aba";
  aba.id = key.replace(/\s+/g, '-');
  aba.style.display = "none";

  values.forEach(item => {
    if (item.titulo) {
      let titulo = document.createElement("div");
      titulo.className = "titulo";
      titulo.innerText = item.titulo; // texto puro
      aba.appendChild(titulo);
    } else if (item.text) {
      let texto = document.createElement("div");
      texto.className = "texto";
      texto.innerText = item.text;
      aba.appendChild(texto);
    }
  });

  if (aba_content) aba_content.appendChild(aba);
});


  document.querySelectorAll(".aba").forEach(aba => {
    let btn = document.createElement("button");
    btn.innerText = aba.id.replace(/-/g, ' ');
    btn.dataset.aba = aba.id;

    btn.addEventListener("click", () => {
      btn.classList.add("active");
      document.querySelectorAll("#aba-wrapper button").forEach(b => {
        if (b !== btn) b.classList.remove("active");
      });
      document.querySelectorAll(".aba").forEach(d => d.style.display = "none");
      aba.style.display = "grid";
    });

    if (aba_wrapper) aba_wrapper.appendChild(btn);
  });

  const first_aba_elem = document.querySelector(".aba");
  if (first_aba_elem) {
    first_aba_elem.style.display = "grid";
    const first_aba = aba_wrapper ? aba_wrapper.querySelector("button") : null;
    if (first_aba) first_aba.classList.add("active");
  }
}).catch(err => console.error(err));

// ======================= EDIT MODE FUNCTIONS =======================
async function get_edit_mode() { return await invoke("edit_mode_get"); }
async function set_edit_mode(value) { await invoke("edit_mode_set", { value }); location.reload(); }

let edit_mode = await get_edit_mode();

document.getElementById("edit-btn").addEventListener("click", async () => {
  if (edit_mode !== "true") {
    await set_edit_mode(true);
  } else {
    const modal = document.getElementById("modal-edit");
    modal.classList.remove("hidden");

    document.getElementById("modal-cancelar").onclick = () => {
      modal.classList.add("hidden");
    };

    document.getElementById("modal-ok").onclick = async () => {
      modal.classList.add("hidden");
      await set_edit_mode(false);
    };
  }
});


// ======================= BOTÃO TEAMVIEWER HOST =======================
function setupTvHost(edit_mode) {
  const tvHostBtn = document.getElementById("tv-host");
  if (!tvHostBtn) return;

  if (edit_mode === "false") {
    // Modo normal: mostra apenas o nome
    tvHostBtn.innerText = tvHostBtn.getAttribute("name");

    // Copia o value ao clicar
    tvHostBtn.addEventListener("click", () => {
      const val = tvHostBtn.value;
      navigator.clipboard.writeText(val).then(() => {
        tvHostBtn.classList.add("copied");
        setTimeout(() => tvHostBtn.classList.remove("copied"), 1000);
      });
    });
  } else if (edit_mode === "true") {
    // Modo edição: mostra o value como editável
    tvHostBtn.innerText = tvHostBtn.value;
    tvHostBtn.setAttribute("contenteditable", "true");

    // Ao perder foco ou ao salvar, atualiza o value
    tvHostBtn.addEventListener("blur", () => {
      tvHostBtn.value = tvHostBtn.innerText.trim();
      // volta a mostrar o nome
      tvHostBtn.innerText = tvHostBtn.value;
    });

    // Integra com seu botão "Salvar"
    const saveBtn = document.getElementById("save-btn");
    if (saveBtn) {
      saveBtn.addEventListener("click", () => {
        tvHostBtn.value = tvHostBtn.innerText.trim();
        // restaura o texto visível para o nome
        tvHostBtn.innerText = tvHostBtn.getAttribute("name");
      });
    }
  
 

  }
}

// Chame depois de obter edit_mode
setupTvHost(edit_mode);


// ======================= WHEN NOT IN EDIT MODE =======================
if (edit_mode === "false") {
  document.querySelectorAll(".texto").forEach(aba => {
    aba.addEventListener("click", () => {
      const text = aba.innerText;
      navigator.clipboard.writeText(text).then(() => {
        aba.classList.add("copied");
        setTimeout(() => aba.classList.remove("copied"), 1000);
      });
    });
  });
}

// ======================= WHEN IN EDIT MODE =======================
if (edit_mode === "true") {

  // Função para limpar formatação ao colar
function handlePasteAsPlainText(e) {
  e.preventDefault();
  const text = e.clipboardData.getData("text/plain");
  document.execCommand("insertText", false, text);
}

// aplica em todos os campos de titulo e texto
document.querySelectorAll(".titulo, .texto").forEach(elem => {
  elem.addEventListener("paste", handlePasteAsPlainText);
});

  let save_button = document.getElementById("save-btn");
  save_button.style.display = "block";

  // Adiciona borda branca 2px em modo edição
  document.querySelectorAll(".texto, .titulo").forEach(el => {
    el.setAttribute("contenteditable", "true");
    el.style.border = "1px solid white";

    el.addEventListener("keydown", function(e) {
      if (el.innerHTML.endsWith("<br>")) {
        document.execCommand("insertHTML", false, "<br>");
      }
      if (e.key === "Enter") {
        e.preventDefault();
        document.execCommand("insertHTML", false, "<br><br>"); // insere <br>
      }
    });

  });
  // Botão salvar
save_button.addEventListener("click", async () => {
  const json_data = {
    tv_host: {
      name: document.getElementById("tv-host").getAttribute("name"),
      value: document.getElementById("tv-host").value
    },
    abas: []
  };

// Função auxiliar para extrair texto preservando quebras de linha
function getTextWithBreaks(elem) {
  let text = "";
  elem.childNodes.forEach(node => {
    if (node.nodeType === Node.TEXT_NODE) {
      text += node.textContent;
    } else if (node.nodeType === Node.ELEMENT_NODE) {
      if (node.tagName === "BR") {
        text += "\n";
      } else {
        text += node.textContent;
      }
    }
  });
  return text;
}

document.querySelectorAll(".aba").forEach(aba => {
  const key = aba.id.replace(/-/g, ' ');
  const values = [];

  aba.querySelectorAll(".titulo, .texto").forEach(elem => {
    if (elem.classList.contains("titulo")) {
      values.push({ titulo: getTextWithBreaks(elem) });
    } else if (elem.classList.contains("texto")) {
      values.push({ text: getTextWithBreaks(elem) });
    }
  });

  json_data.abas.push({ key, values });
});

  try {
    await invoke("set_json", { data: json_data });
    location.reload();
  } catch (err) {
    console.error("Erro ao salvar JSON:", err);
    alert("Falha ao salvar.");
  }
});


  // === CONTEXT MENU ===
  const contextMenu = document.createElement('div');
  contextMenu.id = 'ctx-menu';
  contextMenu.className = 'ctx-menu hidden';
  contextMenu.style.position = 'absolute';
  contextMenu.innerHTML = `
    <div data-action="add-titulo">Adicionar título antes</div>
    <div data-action="add-texto">Adicionar texto antes</div>
    <div data-action="add-titulo-after">Adicionar título depois</div>
    <div data-action="add-texto-after">Adicionar texto depois</div>
    <div data-action="delete">Excluir</div>
  `;
  document.body.appendChild(contextMenu);

  let contextTarget = null;

  function attachContextListener(el) {
    if (!el || el._hasCtx) return;
    el.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      hideContextMenu();
      showContextMenuAt(e.pageX, e.pageY, e.currentTarget);
    });
    el._hasCtx = true;
  }

function hideContextMenu() {
  contextMenu.classList.add('hidden');
  contextMenu.style.display = 'none';

  // remove borda azul do target
  if (contextTarget) {
    contextTarget.style.border = "1px solid white";
  }

  contextTarget = null;
}

function showContextMenuAt(x, y, target) {
  contextTarget = target || null;
  contextMenu.classList.remove('hidden');
  contextMenu.style.display = 'block';

  // adiciona borda azul no target
  if (contextTarget) {
    contextTarget.style.border = "1px solid blue";
  }

  const bodyRect = document.body.getBoundingClientRect();
  const menuRect = contextMenu.getBoundingClientRect();

  let left = x;
  let top = y;

  if (x + menuRect.width > bodyRect.width) left = x - menuRect.width;
  if (y + menuRect.height > bodyRect.height) top = y - menuRect.height;

  contextMenu.style.left = `${left}px`;
  contextMenu.style.top = `${top}px`;
}


  document.querySelectorAll('.texto, .titulo').forEach(elem => attachContextListener(elem));

  contextMenu.addEventListener('click', (ev) => {
    const action = ev.target.getAttribute('data-action');
    if (!action || !contextTarget) return hideContextMenu();

    let newEl;
    if (action.includes('titulo')) {
      newEl = document.createElement('div');
      newEl.className = 'titulo';
      newEl.innerText = 'Novo Título';
    } else if (action.includes('texto')) {
      newEl = document.createElement('div');
      newEl.className = 'texto';
      newEl.innerText = 'Novo texto...';
    }

    if (newEl) {
      newEl.setAttribute('contenteditable', 'true');
      newEl.style.border = "1px solid white"; // mantém borda no novo elemento
      const insertAfter = action.endsWith('after');
      contextTarget.parentNode.insertBefore(newEl, insertAfter ? contextTarget.nextSibling : contextTarget);
      attachContextListener(newEl);
    } else if (action === 'delete') {
      contextTarget.parentNode.removeChild(contextTarget);
    }

    hideContextMenu();
  });

     // ======================= CONTEXT MENU PARA BOTÕES =======================
const btnContextMenu = document.createElement('div');
btnContextMenu.id = 'btn-ctx-menu';
btnContextMenu.style.position = 'fixed';
btnContextMenu.className = 'ctx-menu hidden';
btnContextMenu.innerHTML = `
  <div data-action="rename">Renomear</div>
  <div data-action="delete">Excluir</div>`;
document.body.appendChild(btnContextMenu);

let btnContextTarget = null;

function hideBtnContextMenu() {
  btnContextMenu.classList.add('hidden');
  btnContextMenu.style.display = 'none';
  btnContextTarget = null;
}

function showBtnContextMenuAt(x, y, target) {
  btnContextTarget = target || null;
  btnContextMenu.classList.remove('hidden');
  btnContextMenu.style.display = 'block';
  btnContextMenu.style.left = `${x}px`;
  btnContextMenu.style.top = `${y}px`;
}

// Adiciona listener de contexto para cada botão
document.querySelectorAll('#aba-wrapper button').forEach(btn => {
  btn.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    showBtnContextMenuAt(e.pageX, e.pageY, e.currentTarget);
  });
});

// Ações do menu
btnContextMenu.addEventListener('click', (ev) => {
  const action = ev.target.getAttribute('data-action');
  if (!action || !btnContextTarget) return hideBtnContextMenu();

  const abaId = btnContextTarget.dataset.aba;
  const abaElem = document.getElementById(abaId);

  if (action === 'delete') {
    // Remove botão e aba correspondente
    abaElem?.remove();
    btnContextTarget.remove();
  } else if (action === 'rename') {
    const novoNome = prompt("Novo nome da aba:", btnContextTarget.innerText);
    if (novoNome && novoNome.trim()) {
      const newId = novoNome.trim().replace(/\s+/g, '-');
      // Atualiza botão
      btnContextTarget.innerText = novoNome.trim();
      btnContextTarget.dataset.aba = newId;
      // Atualiza aba
      if (abaElem) {
        abaElem.id = newId;
      }
    }
  }
  hideBtnContextMenu();
});

// Fecha menu ao clicar fora ou apertar ESC
document.addEventListener('click', () => hideBtnContextMenu());
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') hideBtnContextMenu(); });


// ======================= MODAL PARA NOVA ABA =======================
const modal = document.getElementById("modal");
const input = document.getElementById("new-aba-name");
const confirmBtn = document.getElementById("modal-confirm");
const cancelBtn = document.getElementById("modal-cancel");
const newAbaBtn = document.getElementById("new-btn");

newAbaBtn.style.display = "block";
newAbaBtn.addEventListener("click", () => {
  modal.classList.remove("hidden");
  input.value = "";
  input.focus();
});
cancelBtn.addEventListener("click", () => {
  modal.classList.add("hidden");
});

confirmBtn.addEventListener("click", () => {
  const new_aba = input.value.trim();
  if (new_aba) {
    let aba = document.createElement("div");
    aba.className = "aba";
    aba.id = new_aba.replace(/\s+/g, '-');
    aba.style.display = "none";

    let titulo = document.createElement("div");
    titulo.className = "titulo";
    titulo.innerText = "Nova Aba";
    titulo.setAttribute("contenteditable", "true");
    attachContextListener(titulo);

    let texto = document.createElement("div");
    texto.className = "texto";
    texto.innerText = "Escreva aqui...";
    texto.setAttribute("contenteditable", "true");
    attachContextListener(texto);

    aba.appendChild(titulo);
    aba.appendChild(texto);
    if (aba_content) aba_content.appendChild(aba);

    let btn = document.createElement("button");
    btn.innerText = new_aba;
    btn.addEventListener("click", () => {
      btn.classList.add("active");
      document.querySelectorAll("#aba-wrapper button").forEach(b => {
        if (b !== btn) b.classList.remove("active");
      });
      document.querySelectorAll(".aba").forEach(d => d.style.display = "none");
      aba.style.display = "grid";
    });
    if (aba_wrapper) aba_wrapper.appendChild(btn);
  }
  modal.classList.add("hidden");
});

  document.addEventListener('click', hideContextMenu);
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') hideContextMenu(); });

  
}
