frappe.ui.form.on("Assessment Question", {
	refresh(frm) {
		if (frm.is_new()) return;
		frm.add_custom_button(__("Suggest Translations"), () => frappe.call({
			method: "moet_careers.i18n.suggest_assessment_translations",
			args: { doctype: frm.doctype, name: frm.doc.name },
			callback: (r) => frappe.show_alert({ message: r.message, indicator: "blue" }, 8),
		}));
		if (!frm._moet_rt) {
			frm._moet_rt = true;
			frappe.realtime.on("moet_translations_ready", (data) => {
				if (cur_frm && cur_frm.doc.name === data.job) {
					frappe.show_alert({ message: data.message, indicator: "green" }, 8);
					if (!cur_frm.is_dirty()) cur_frm.reload_doc();
				}
			});
		}
	},
});
